using System;
using System.Collections.Generic;
using System.Threading;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderFramePipeline
    {
        private readonly Queue<QueuedFrameOutput> pendingFrames = new Queue<QueuedFrameOutput>();
        private readonly Queue<byte[]> frameBufferPool = new Queue<byte[]>();
        private readonly object frameBufferPoolLock = new object();
        private readonly int frameByteLength;
        private readonly int maxPendingGpuFrames;
        private byte[]? lastFrameBytes;
        private int pendingGpuFrames;

        public ChartRenderFramePipeline(ChartRenderMemoryBudget budget)
        {
            frameByteLength = (int)Math.Max(1L, Math.Min(int.MaxValue, budget.FrameBytes));
            maxPendingGpuFrames = Math.Max(1, budget.MaxPendingGpuFrames);
        }

        public int PendingCount => pendingFrames.Count;

        public int WrittenFrames { get; private set; }

        public int DuplicateFrames { get; private set; }

        public void Reset()
        {
            pendingFrames.Clear();
            pendingGpuFrames = 0;
            lastFrameBytes = null;
            WrittenFrames = 0;
            DuplicateFrames = 0;
        }

        public void RequestFrame(IChartFrameCapture frameCapture, int index)
        {
            pendingFrames.Enqueue(QueuedFrameOutput.Capture(frameCapture.RequestFrame(index)));
            pendingGpuFrames++;
        }

        public void RepeatLastFrame(int index)
        {
            pendingFrames.Enqueue(QueuedFrameOutput.Repeat(index));
        }

        public void DrainReadyFrames(FfmpegEncoder encoder, Func<bool> isCancelRequested)
        {
            while (pendingFrames.Count > 0)
            {
                QueuedFrameOutput output = pendingFrames.Peek();
                if (!output.Done)
                {
                    return;
                }

                pendingFrames.Dequeue();
                if (output.PendingFrame != null)
                {
                    pendingGpuFrames = Math.Max(0, pendingGpuFrames - 1);
                    WriteCapturedFrame(output.PendingFrame, encoder, isCancelRequested);
                }
                else
                {
                    WriteRepeatedFrame(output.Index, encoder, isCancelRequested);
                }
            }
        }

        public void WaitForPendingSlot(FfmpegEncoder encoder, Func<bool> isCancelRequested)
        {
            while (pendingGpuFrames >= maxPendingGpuFrames && !isCancelRequested())
            {
                int before = pendingGpuFrames;
                DrainReadyFrames(encoder, isCancelRequested);
                if (pendingGpuFrames < maxPendingGpuFrames)
                {
                    return;
                }

                if (pendingGpuFrames == before)
                {
                    Thread.Sleep(1);
                }
            }
        }

        public void Clear()
        {
            pendingFrames.Clear();
            pendingGpuFrames = 0;
        }

        private void WriteCapturedFrame(ChartPendingFrame pending, FfmpegEncoder encoder, Func<bool> isCancelRequested)
        {
            byte[] buffer = RentFrameBuffer();
            try
            {
                pending.Complete(buffer);
                EnsureLastFrameBuffer();
                Buffer.BlockCopy(buffer, 0, lastFrameBytes!, 0, pending.ByteLength);
                encoder.WriteFrame(buffer, pending.ByteLength, pending.RepeatCount, ReturnFrameBuffer, isCancelRequested);
                buffer = null!;
            }
            finally
            {
                if (buffer != null)
                {
                    ReturnFrameBuffer(buffer);
                }
            }

            WrittenFrames += pending.RepeatCount;
            DuplicateFrames += Math.Max(0, pending.RepeatCount - 1);
        }

        private void WriteRepeatedFrame(int index, FfmpegEncoder encoder, Func<bool> isCancelRequested)
        {
            if (lastFrameBytes == null)
            {
                throw new InvalidOperationException("Cannot repeat chart render frame " + index + " before a frame has been captured.");
            }

            byte[] buffer = RentFrameBuffer();
            try
            {
                Buffer.BlockCopy(lastFrameBytes, 0, buffer, 0, frameByteLength);
                encoder.WriteFrame(buffer, frameByteLength, 1, ReturnFrameBuffer, isCancelRequested);
                buffer = null!;
            }
            finally
            {
                if (buffer != null)
                {
                    ReturnFrameBuffer(buffer);
                }
            }

            WrittenFrames++;
        }

        private void EnsureLastFrameBuffer()
        {
            if (lastFrameBytes == null || lastFrameBytes.Length < frameByteLength)
            {
                lastFrameBytes = new byte[frameByteLength];
            }
        }

        private byte[] RentFrameBuffer()
        {
            lock (frameBufferPoolLock)
            {
                if (frameBufferPool.Count > 0)
                {
                    return frameBufferPool.Dequeue();
                }
            }

            return new byte[frameByteLength];
        }

        private void ReturnFrameBuffer(byte[] buffer)
        {
            if (buffer.Length < frameByteLength)
            {
                return;
            }

            lock (frameBufferPoolLock)
            {
                frameBufferPool.Enqueue(buffer);
            }
        }

        private sealed class QueuedFrameOutput
        {
            private QueuedFrameOutput(ChartPendingFrame? pendingFrame, int index)
            {
                PendingFrame = pendingFrame;
                Index = index;
            }

            public ChartPendingFrame? PendingFrame { get; }

            public int Index { get; }

            public bool Done => PendingFrame == null || PendingFrame.Done;

            public static QueuedFrameOutput Capture(ChartPendingFrame pendingFrame)
            {
                return new QueuedFrameOutput(pendingFrame, pendingFrame.Index);
            }

            public static QueuedFrameOutput Repeat(int index)
            {
                return new QueuedFrameOutput(null, index);
            }
        }
    }
}
