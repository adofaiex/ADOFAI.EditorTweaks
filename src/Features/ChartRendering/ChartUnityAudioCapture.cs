using System;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartUnityAudioCapture : IDisposable
    {
        private const int NoSampleGraceFrames = 30;

        private readonly string path;
        private readonly int sampleRate;
        private readonly int channelCount;
        private FileStream? stream;
        private NativeArray<float> samples;
        private float[]? managedSamples;
        private byte[]? sampleBytes;
        private long dataBytes;
        private int captureFrameCount;
        private int consecutiveZeroSampleFrames;
        private bool started;

        public ChartUnityAudioCapture(string path)
        {
            this.path = path;
            sampleRate = AudioSettings.outputSampleRate;
            channelCount = GetChannelCount(AudioSettings.speakerMode);
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
            started = true;
            ChartRenderDiagnostics.Log("AudioRenderer capture started. sampleRate=" + sampleRate + " channels=" + channelCount + ".");
        }

        public void CaptureFrame()
        {
            if (!started || stream == null)
            {
                return;
            }

            int sampleCount = AudioRenderer.GetSampleCountForCaptureFrame();
            int floatCount = Math.Max(0, sampleCount * channelCount);
            if (floatCount == 0)
            {
                captureFrameCount++;
                consecutiveZeroSampleFrames++;
                if (consecutiveZeroSampleFrames == 1 || consecutiveZeroSampleFrames % 10 == 0)
                {
                    ChartRenderDiagnostics.Log("AudioRenderer returned zero samples. captureFrame=" + captureFrameCount
                        + " consecutiveZeroFrames=" + consecutiveZeroSampleFrames + ".");
                }

                if (consecutiveZeroSampleFrames >= NoSampleGraceFrames)
                {
                    throw new InvalidOperationException("Unity AudioRenderer returned no samples for "
                        + consecutiveZeroSampleFrames + " consecutive capture frames.");
                }

                return;
            }

            EnsureBuffers(floatCount);
            if (!AudioRenderer.Render(samples))
            {
                throw new InvalidOperationException("Unity AudioRenderer failed to render capture frame "
                    + captureFrameCount + ".");
            }

            captureFrameCount++;
            consecutiveZeroSampleFrames = 0;
            samples.CopyTo(managedSamples!);
            Buffer.BlockCopy(managedSamples!, 0, sampleBytes!, 0, floatCount * sizeof(float));
            stream.Write(sampleBytes!, 0, floatCount * sizeof(float));
            dataBytes += floatCount * sizeof(float);
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
