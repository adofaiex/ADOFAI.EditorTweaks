using System;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartUnityAudioCapture : IDisposable
    {
        private const int RecoveryDelaySeconds = 1;
        private const int NoSampleGraceSecondsAfterRecovery = 5;

        private readonly string path;
        private readonly int sampleRate;
        private readonly int channelCount;
        private readonly int framesPerSecond;
        private readonly int recoveryDelayFrames;
        private readonly int noSampleGraceFramesAfterRecovery;
        private FileStream? stream;
        private NativeArray<float> samples;
        private float[]? managedSamples;
        private byte[]? sampleBytes;
        private long dataBytes;
        private int captureFrameCount;
        private int consecutiveZeroSampleFrames;
        private bool recoveryAttempted;
        private bool started;

        public ChartUnityAudioCapture(string path, int framesPerSecond)
        {
            this.path = path;
            this.framesPerSecond = Math.Max(1, framesPerSecond);
            sampleRate = AudioSettings.outputSampleRate;
            channelCount = GetChannelCount(AudioSettings.speakerMode);
            recoveryDelayFrames = Math.Max(1, this.framesPerSecond * RecoveryDelaySeconds);
            noSampleGraceFramesAfterRecovery = Math.Max(1, this.framesPerSecond * NoSampleGraceSecondsAfterRecovery);
        }

        public string Path => path;

        public int SampleRate => sampleRate;

        public int ChannelCount => channelCount;

        public long CapturedSampleFrames => channelCount <= 0 ? 0L : dataBytes / (channelCount * sizeof(float));

        public double CapturedSeconds => sampleRate <= 0 ? 0.0 : CapturedSampleFrames / (double)sampleRate;

        public int ConsecutiveZeroSampleFrames => consecutiveZeroSampleFrames;

        public void Begin()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
            stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            WriteHeader(dataSize: 0);
            dataBytes = 0;
            if (!AudioRenderer.Start())
            {
                ChartRenderDiagnostics.Log("AudioRenderer.Start returned false; attempting one recovery.");
                try
                {
                    AudioRenderer.Stop();
                }
                catch
                {
                }

                if (!AudioRenderer.Start())
                {
                    stream.Dispose();
                    stream = null;
                    throw new InvalidOperationException("Unity AudioRenderer could not enter capture mode.");
                }

                ChartRenderDiagnostics.Log("AudioRenderer recovery succeeded.");
            }

            captureFrameCount = 0;
            consecutiveZeroSampleFrames = 0;
            recoveryAttempted = false;
            started = true;
            int dspBufferSize = AudioSettings.GetConfiguration().dspBufferSize;
            ChartRenderDiagnostics.Log("AudioRenderer capture started. sampleRate=" + sampleRate
                + " channels=" + channelCount
                + " fps=" + framesPerSecond
                + " dspBufferSize=" + dspBufferSize
                + " recoveryFrames=" + recoveryDelayFrames
                + " graceFramesAfterRecovery=" + noSampleGraceFramesAfterRecovery + ".");
        }

        public void CaptureFrame()
        {
            if (!started || stream == null)
            {
                return;
            }

            captureFrameCount++;
            int sampleCount = AudioRenderer.GetSampleCountForCaptureFrame();
            int floatCount = Math.Max(0, sampleCount * channelCount);
            if (floatCount == 0)
            {
                HandleUnavailableCaptureFrame("returned zero samples");
                return;
            }

            EnsureBuffers(floatCount);
            if (!AudioRenderer.Render(samples))
            {
                throw new InvalidOperationException("Unity AudioRenderer failed to render capture frame "
                    + captureFrameCount + ".");
            }

            consecutiveZeroSampleFrames = 0;
            samples.CopyTo(managedSamples!);
            int bytesToWrite = floatCount * sizeof(float);
            Buffer.BlockCopy(managedSamples!, 0, sampleBytes!, 0, bytesToWrite);
            stream.Write(sampleBytes!, 0, bytesToWrite);
            dataBytes += bytesToWrite;
        }

        public void Complete()
        {
            if (stream == null)
            {
                return;
            }

            stream.Position = 0;
            WriteHeader(dataBytes);
            stream.Flush();
            stream.Dispose();
            stream = null;
        }

        public void Dispose()
        {
            if (started)
            {
                try
                {
                    AudioRenderer.Stop();
                }
                catch
                {
                }

                started = false;
            }

            if (samples.IsCreated)
            {
                samples.Dispose();
            }

            stream?.Dispose();
            stream = null;
        }

        private void EnsureBuffers(int floatCount)
        {
            if (samples.IsCreated && samples.Length == floatCount)
            {
                return;
            }

            if (samples.IsCreated)
            {
                samples.Dispose();
            }

            samples = new NativeArray<float>(floatCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            managedSamples = new float[floatCount];
            sampleBytes = new byte[floatCount * sizeof(float)];
        }

        private void HandleUnavailableCaptureFrame(string reason)
        {
            consecutiveZeroSampleFrames++;
            if (consecutiveZeroSampleFrames == 1 || consecutiveZeroSampleFrames % Math.Max(1, framesPerSecond) == 0)
            {
                ChartRenderDiagnostics.Log("AudioRenderer " + reason + ". captureFrame=" + captureFrameCount
                    + " consecutiveUnavailableFrames=" + consecutiveZeroSampleFrames + ".");
            }

            if (!recoveryAttempted && consecutiveZeroSampleFrames >= recoveryDelayFrames)
            {
                RestartAudioRenderer();
                recoveryAttempted = true;
                consecutiveZeroSampleFrames = 0;
                ChartRenderDiagnostics.Log("AudioRenderer recovery succeeded; capture will continue.");
                return;
            }

            if (recoveryAttempted && consecutiveZeroSampleFrames >= noSampleGraceFramesAfterRecovery)
            {
                throw new InvalidOperationException("Unity AudioRenderer remained unavailable for "
                    + NoSampleGraceSecondsAfterRecovery
                    + " seconds after recovery. The render was stopped to avoid producing a fully silent video.");
            }
        }

        private void RestartAudioRenderer()
        {
            ChartRenderDiagnostics.Log("AudioRenderer produced no usable samples for "
                + RecoveryDelaySeconds + " second; attempting recovery.");
            try
            {
                AudioRenderer.Stop();
            }
            catch
            {
            }

            if (!AudioRenderer.Start())
            {
                throw new InvalidOperationException("Unity AudioRenderer could not recover after returning no samples.");
            }
        }

        private void WriteHeader(long dataSize)
        {
            if (stream == null)
            {
                return;
            }

            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write((uint)Math.Min(uint.MaxValue, 36L + dataSize));
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16u);
                writer.Write((ushort)3);
                writer.Write((ushort)channelCount);
                writer.Write((uint)sampleRate);
                writer.Write((uint)(sampleRate * channelCount * sizeof(float)));
                writer.Write((ushort)(channelCount * sizeof(float)));
                writer.Write((ushort)32);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write((uint)Math.Min(uint.MaxValue, dataSize));
            }
        }

        private static int GetChannelCount(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono:
                    return 1;
                case AudioSpeakerMode.Quad:
                    return 4;
                case AudioSpeakerMode.Surround:
                    return 5;
                case AudioSpeakerMode.Mode5point1:
                    return 6;
                case AudioSpeakerMode.Mode7point1:
                    return 8;
                case AudioSpeakerMode.Prologic:
                case AudioSpeakerMode.Stereo:
                default:
                    return 2;
            }
        }
    }
}
