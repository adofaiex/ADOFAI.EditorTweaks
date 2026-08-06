using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace EditorTweaks.Features.ChartRendering
{
    internal sealed class FfmpegEncoder : IDisposable
    {
        private static readonly object EncoderProbeLock = new object();
        private static bool? nvencAvailable;

        private readonly string ffmpegPath;
        private readonly string tempVideoPath;
        private readonly string finalVideoPath;
        private readonly int width;
        private readonly int height;
        private readonly int fps;
        private readonly int crf;
        private readonly int configuredBitrateMbps;
        private readonly string encoderMode;
        private readonly string customPreset;
        private readonly string inputPixelFormat;
        private readonly bool flipVertically;
        private readonly int queueCapacityFrames;
        private readonly string audioFormat;
        private readonly string customMuxArgs;
        private readonly float audioSyncOffsetMs;
        private Process? process;
        private BlockingCollection<QueuedFrame>? frameQueue;
        private Thread? writerThread;
        private Exception? writerException;

        public FfmpegEncoder(
            string ffmpegPath,
            string tempVideoPath,
            string finalVideoPath,
            int width,
            int height,
            int fps,
            int crf,
            int bitrateMbps,
            string encoderMode,
            string customPreset,
            string inputPixelFormat,
            bool flipVertically,
            int queueCapacityFrames,
            float audioSyncOffsetMs,
            string audioFormat,
            string customMuxArgs)
        {
            this.ffmpegPath = ffmpegPath;
            this.tempVideoPath = tempVideoPath;
            this.finalVideoPath = finalVideoPath;
            this.width = width;
            this.height = height;
            this.fps = fps;
            this.crf = crf;
            configuredBitrateMbps = Math.Max(ChartRenderBitratePresets.AutoBitrateMbps, Math.Min(ChartRenderBitratePresets.MaxBitrateMbps, bitrateMbps));
            this.encoderMode = ChartRenderOptionValues.NormalizeEncoderMode(encoderMode);
            this.customPreset = string.IsNullOrWhiteSpace(customPreset) ? "veryfast" : customPreset.Trim();
            this.inputPixelFormat = ChartRenderOptionValues.NormalizeCaptureFormat(inputPixelFormat);
            this.flipVertically = flipVertically;
            this.queueCapacityFrames = Math.Max(1, queueCapacityFrames);
            this.audioSyncOffsetMs = audioSyncOffsetMs;
            this.audioFormat = ChartRenderOptionValues.NormalizeAudioFormat(audioFormat);
            this.customMuxArgs = (customMuxArgs ?? string.Empty).Trim();
        }

        public string EncoderName { get; private set; } = "unknown";

        public void BeginVideo()
        {
            string args = "-y -f rawvideo -pixel_format " + inputPixelFormat + " "
                + "-video_size " + width + "x" + height + " "
                + "-framerate " + fps + " "
                + "-i - -an -vf " + Quote(GetVideoFilter()) + " "
                + GetVideoEncoderArguments() + " "
                + "-pix_fmt yuv420p "
                + Quote(tempVideoPath);

            ChartRenderDiagnostics.Log("FFmpeg video args: mode=" + encoderMode
                + " input=" + inputPixelFormat
                + " verticalFlip=" + flipVertically
                + " bitrate=" + GetTargetBitrateMbps() + "M"
                + " queueFrames=" + queueCapacityFrames
                + " args=" + args);
            process = StartProcess(args, redirectInput: true);
            frameQueue = new BlockingCollection<QueuedFrame>(boundedCapacity: queueCapacityFrames);
            writerException = null;
            writerThread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "EditorTweaks.FFmpegVideoWriter"
            };
            writerThread.Start();
        }

        private string GetVideoFilter()
        {
            const string evenSizePadding = "pad=ceil(iw/2)*2:ceil(ih/2)*2";
            return flipVertically ? "vflip," + evenSizePadding : evenSizePadding;
        }

        public void WriteFrame(byte[] frame, int length, int repeatCount, Action<byte[]>? release, Func<bool>? isCancelRequested = null)
        {
            QueuedFrame queuedFrame = new QueuedFrame(frame, length, Math.Max(1, repeatCount), release);
            while (true)
            {
                if (isCancelRequested != null && isCancelRequested())
                {
                    throw new OperationCanceledException("Canceled.");
                }

                if (process == null || process.HasExited)
                {
                    throw new InvalidOperationException("FFmpeg video process is not running.");
                }

                if (writerException != null)
                {
                    throw new InvalidOperationException("FFmpeg writer failed.", writerException);
                }

                if (frameQueue!.TryAdd(queuedFrame, 50))
                {
                    return;
                }
            }
        }

        public void CompleteVideo()
        {
            if (process == null)
            {
                return;
            }

            frameQueue?.CompleteAdding();
            writerThread?.Join();
            if (writerException != null)
            {
                throw new InvalidOperationException("FFmpeg writer failed.", writerException);
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("FFmpeg video encoding failed with exit code " + process.ExitCode + ".");
            }

            process.Dispose();
            process = null;
            frameQueue?.Dispose();
            frameQueue = null;
            writerThread = null;
        }

        public void MuxAudioFile(string audioPath)
        {
            if (!File.Exists(audioPath))
            {
                throw new InvalidOperationException("Captured audio file does not exist: " + audioPath);
            }

            long videoBytes = File.Exists(tempVideoPath) ? new FileInfo(tempVideoPath).Length : 0L;
            long audioBytes = new FileInfo(audioPath).Length;
            ChartRenderDiagnostics.Log("Muxing captured audio. videoBytes=" + videoBytes + " audioBytes=" + audioBytes + " audioSyncOffsetMs=" + Number(audioSyncOffsetMs));

            string args = BuildMuxArguments(audioPath);
            RunMuxProcess(args);
        }

        private string BuildMuxArguments(string audioPath)
        {
            string baseInputs = "-y -i " + Quote(tempVideoPath) + " -i " + Quote(audioPath) + " ";
            string outputArgs = (!string.IsNullOrWhiteSpace(customMuxArgs)
                    ? customMuxArgs + " "
                    : "-c:v copy " + GetAudioCodecArguments() + " -movflags +faststart ")
                + Quote(finalVideoPath);

            if (Math.Abs(audioSyncOffsetMs) < 0.001f)
            {
                if (!string.IsNullOrWhiteSpace(customMuxArgs))
                {
                    return baseInputs + outputArgs;
                }

                return baseInputs + "-map 0:v:0 -map 1:a:0 " + outputArgs;
            }

            string filter;
            if (audioSyncOffsetMs > 0f)
            {
                filter = "[1:a]atrim=start=" + Number(audioSyncOffsetMs * 0.001f) + ",asetpts=PTS-STARTPTS[a]";
            }
            else
            {
                int delayMs = Math.Max(0, (int)Math.Round(Math.Abs(audioSyncOffsetMs)));
                filter = "[1:a]adelay=" + delayMs.ToString(CultureInfo.InvariantCulture) + ":all=1,asetpts=PTS-STARTPTS[a]";
            }

            if (!string.IsNullOrWhiteSpace(customMuxArgs))
            {
                return baseInputs + "-filter_complex " + Quote(filter) + " " + outputArgs;
            }

            return baseInputs + "-filter_complex " + Quote(filter) + " -map 0:v:0 -map " + Quote("[a]") + " " + outputArgs;
        }

        public void Dispose()
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                }

                process.Dispose();
                process = null;
            }

            if (frameQueue != null)
            {
                if (!frameQueue.IsAddingCompleted)
                {
                    frameQueue.CompleteAdding();
                }

                frameQueue.Dispose();
                frameQueue = null;
            }
        }

        private void WriteLoop()
        {
            try
            {
                foreach (QueuedFrame frame in frameQueue!.GetConsumingEnumerable())
                {
                    try
                    {
                        for (int i = 0; i < frame.RepeatCount; i++)
                        {
                            process!.StandardInput.BaseStream.Write(frame.Buffer, 0, frame.Length);
                        }
                    }
                    finally
                    {
                        frame.Release?.Invoke(frame.Buffer);
                    }
                }

                process!.StandardInput.Close();
            }
            catch (Exception ex)
            {
                writerException = ex;
            }
        }

        private string GetRealtimePreset()
        {
            if (string.IsNullOrWhiteSpace(customPreset))
            {
                return "ultrafast";
            }

            string lowered = customPreset.ToLowerInvariant();
            if (lowered == "cpu" || lowered == "x264" || lowered == "veryfast")
            {
                return "ultrafast";
            }

            if (lowered.StartsWith("x264:", StringComparison.Ordinal))
            {
                string x264Preset = customPreset.Substring("x264:".Length).Trim();
                return string.IsNullOrWhiteSpace(x264Preset) ? "ultrafast" : x264Preset;
            }

            return customPreset;
        }

        private string GetAudioCodecArguments()
        {
            switch (audioFormat)
            {
                case ChartRenderOptionValues.AudioFormatFlac:
                    return "-c:a flac";
                case ChartRenderOptionValues.AudioFormatAlac:
                    return "-c:a alac";
                default:
                    return "-c:a aac -b:a 320k -ac 2";
            }
        }

        private string GetVideoEncoderArguments()
        {
            if (encoderMode == ChartRenderOptionValues.EncoderCustom)
            {
                return GetCustomVideoEncoderArguments();
            }

            if (!ForcesCpuEncoder() && IsNvencAvailable())
            {
                string nvencPreset = GetNvencPreset();
                string tune = encoderMode == ChartRenderOptionValues.EncoderFastest ? "ll" : "hq";
                EncoderName = "h264_nvenc " + nvencPreset;
                Main.Log("Chart renderer using h264_nvenc preset " + nvencPreset + ".");
                return "-c:v h264_nvenc -preset " + nvencPreset + " -tune " + tune + " " + GetNvencBitrateArguments() + " -g " + Math.Max(1, fps * 2);
            }

            string x264Preset = GetX264Preset();
            EncoderName = "libx264 " + x264Preset;
            Main.Log("Chart renderer using libx264 preset " + x264Preset + ".");
            return "-c:v libx264 -preset " + Quote(x264Preset) + " " + GetX264BitrateArguments() + " -g " + Math.Max(1, fps * 2);
        }

        private string GetCustomVideoEncoderArguments()
        {
            if (!ForcesCustomCpuEncoder() && IsNvencAvailable())
            {
                EncoderName = "h264_nvenc p1";
                Main.Log("Chart renderer using custom compatibility h264_nvenc.");
                return "-c:v h264_nvenc -preset p1 -tune ll " + GetNvencBitrateArguments() + " -g " + Math.Max(1, fps * 2);
            }

            string x264Preset = GetRealtimePreset();
            EncoderName = "libx264 " + x264Preset;
            Main.Log("Chart renderer using custom libx264 preset " + x264Preset + ".");
            return "-c:v libx264 -preset " + Quote(x264Preset) + " " + GetX264BitrateArguments() + " -g " + Math.Max(1, fps * 2);
        }

        private bool ForcesCpuEncoder()
        {
            return encoderMode == ChartRenderOptionValues.EncoderCpuCompatibility;
        }

        private bool ForcesCustomCpuEncoder()
        {
            string lowered = customPreset.ToLowerInvariant();
            return lowered == "cpu" || lowered == "x264" || lowered.StartsWith("x264:", StringComparison.Ordinal);
        }

        private string GetNvencPreset()
        {
            switch (encoderMode)
            {
                case ChartRenderOptionValues.EncoderFastest:
                    return "p1";
                case ChartRenderOptionValues.EncoderQuality:
                    return "p6";
                case ChartRenderOptionValues.EncoderBalanced:
                case ChartRenderOptionValues.EncoderAutoBalanced:
                default:
                    return "p4";
            }
        }

        private string GetX264Preset()
        {
            switch (encoderMode)
            {
                case ChartRenderOptionValues.EncoderFastest:
                    return "ultrafast";
                case ChartRenderOptionValues.EncoderQuality:
                    return "fast";
                case ChartRenderOptionValues.EncoderCpuCompatibility:
                case ChartRenderOptionValues.EncoderBalanced:
                case ChartRenderOptionValues.EncoderAutoBalanced:
                default:
                    return "veryfast";
            }
        }

        private int ClampCrf()
        {
            return Math.Max(0, Math.Min(51, crf));
        }

        private int GetTargetBitrateMbps()
        {
            return ChartRenderBitratePresets.ResolveTargetBitrateMbps(configuredBitrateMbps, width, height, fps);
        }

        private string GetNvencBitrateArguments()
        {
            int target = GetTargetBitrateMbps();
            int maxRate = ChartRenderBitratePresets.GetMaxRateMbps(target);
            int buffer = ChartRenderBitratePresets.GetBufferSizeMbps(target);
            return "-rc vbr -cq " + ClampCrf()
                + " -b:v " + target + "M"
                + " -maxrate " + maxRate + "M"
                + " -bufsize " + buffer + "M";
        }

        private string GetX264BitrateArguments()
        {
            int target = GetTargetBitrateMbps();
            int maxRate = ChartRenderBitratePresets.GetMaxRateMbps(target);
            int buffer = ChartRenderBitratePresets.GetBufferSizeMbps(target);
            return "-b:v " + target + "M"
                + " -maxrate " + maxRate + "M"
                + " -bufsize " + buffer + "M";
        }

        private bool IsNvencAvailable()
        {
            lock (EncoderProbeLock)
            {
                if (nvencAvailable.HasValue)
                {
                    return nvencAvailable.Value;
                }

                nvencAvailable = ProbeEncoder("h264_nvenc");
                return nvencAvailable.Value;
            }
        }

        private bool ProbeEncoder(string encoder)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-hide_banner -loglevel error -y -f lavfi -i color=size=256x256:rate=1:duration=0.1 -c:v " + encoder + " -frames:v 1 -f null -",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using (Process? probe = Process.Start(info))
                {
                    if (probe == null)
                    {
                        return false;
                    }

                    if (!probe.WaitForExit(5000))
                    {
                        try
                        {
                            probe.Kill();
                        }
                        catch
                        {
                        }

                        return false;
                    }

                    return probe.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private sealed class QueuedFrame
        {
            public QueuedFrame(byte[] buffer, int length, int repeatCount, Action<byte[]>? release)
            {
                Buffer = buffer;
                Length = length;
                RepeatCount = repeatCount;
                Release = release;
            }

            public byte[] Buffer { get; }

            public int Length { get; }

            public int RepeatCount { get; }

            public Action<byte[]>? Release { get; }
        }

        private Process StartProcess(string arguments, bool redirectInput)
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -loglevel warning " + arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = redirectInput
            };

            Process? started = Process.Start(info);
            if (started == null)
            {
                throw new InvalidOperationException("Failed to start FFmpeg.");
            }

            return started;
        }

        private void RunMuxProcess(string arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -loglevel warning " + arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            StringBuilder stderr = new StringBuilder();
            StringBuilder stdout = new StringBuilder();
            ChartRenderDiagnostics.Log("FFmpeg mux args: " + arguments);

            Process? mux = Process.Start(info);
            if (mux == null)
            {
                throw new InvalidOperationException("Failed to start FFmpeg.");
            }

            process = mux;
            try
            {
                using (mux)
                {
                    mux.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data != null)
                        {
                            stderr.AppendLine(e.Data);
                        }
                    };
                    mux.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data != null)
                        {
                            stdout.AppendLine(e.Data);
                        }
                    };
                    mux.BeginErrorReadLine();
                    mux.BeginOutputReadLine();
                    mux.WaitForExit();

                    if (mux.ExitCode != 0)
                    {
                        string detail = BuildProcessFailureDetail(stdout.ToString(), stderr.ToString());
                        ChartRenderDiagnostics.Log("FFmpeg audio mux failed with exit code " + mux.ExitCode + "." + detail);
                        throw new InvalidOperationException("FFmpeg audio mux failed with exit code " + mux.ExitCode + "." + detail);
                    }
                }
            }
            finally
            {
                process = null;
            }
        }

        private static string BuildProcessFailureDetail(string stdout, string stderr)
        {
            StringBuilder detail = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                detail.AppendLine().Append("stderr: ").Append(stderr.Trim());
            }

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                detail.AppendLine().Append("stdout: ").Append(stdout.Trim());
            }

            return detail.ToString();
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string Number(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
