using System;

namespace EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderMemoryBudget
    {
        private const long Mib = 1024L * 1024L;
        private const long FullHdPixels = 1920L * 1080L;
        private const long QuadHdPixels = 2560L * 1440L;
        private const long UltraHdPixels = 3840L * 2160L;

        private ChartRenderMemoryBudget(int width, int height, long frameBytes, long budgetBytes, int maxPendingGpuFrames, int maxEncoderQueueFrames)
        {
            Width = width;
            Height = height;
            FrameBytes = frameBytes;
            BudgetBytes = budgetBytes;
            MaxPendingGpuFrames = maxPendingGpuFrames;
            MaxEncoderQueueFrames = maxEncoderQueueFrames;
        }

        public int Width { get; }

        public int Height { get; }

        public long FrameBytes { get; }

        public long BudgetBytes { get; }

        public int MaxPendingGpuFrames { get; }

        public int MaxEncoderQueueFrames { get; }

        public string LogSummary =>
            "frame=" + FormatBytes(FrameBytes)
            + ", budget=" + FormatBytes(BudgetBytes)
            + ", gpuPending=" + MaxPendingGpuFrames
            + ", ffmpegQueue=" + MaxEncoderQueueFrames;

        public string DisplaySummary =>
            "单帧 " + FormatBytes(FrameBytes) + " / 缓存上限 " + FormatBytes(BudgetBytes);

        public string QueueSummary =>
            "GPU 回读最多 " + MaxPendingGpuFrames + " 帧 / 编码缓存最多 " + MaxEncoderQueueFrames + " 帧";

        public static ChartRenderMemoryBudget Create(int width, int height)
        {
            long pixels = Math.Max(1L, (long)Math.Max(1, width) * Math.Max(1, height));
            long frameBytes = checked(pixels * 4L);
            long budgetBytes = GetDefaultBudgetBytes(pixels);
            int framesByBudget = Math.Max(1, (int)Math.Min(120L, budgetBytes / Math.Max(1L, frameBytes)));
            int baseGpuPending = GetBasePendingGpuFrames(pixels);
            int pendingByBudget = Math.Max(1, framesByBudget / 2);
            int maxPendingGpuFrames = Math.Max(1, Math.Min(baseGpuPending, pendingByBudget));
            int maxEncoderQueueFrames = Math.Max(2, Math.Min(framesByBudget, pixels <= FullHdPixels ? 90 : 32));

            return new ChartRenderMemoryBudget(width, height, frameBytes, budgetBytes, maxPendingGpuFrames, maxEncoderQueueFrames);
        }

        private static long GetDefaultBudgetBytes(long pixels)
        {
            if (pixels <= FullHdPixels)
            {
                return 384L * Mib;
            }

            if (pixels <= UltraHdPixels)
            {
                return 512L * Mib;
            }

            return 768L * Mib;
        }

        private static int GetBasePendingGpuFrames(long pixels)
        {
            if (pixels <= FullHdPixels)
            {
                return 8;
            }

            if (pixels <= QuadHdPixels)
            {
                return 6;
            }

            if (pixels <= UltraHdPixels)
            {
                return 4;
            }

            return 2;
        }

        private static string FormatBytes(long bytes)
        {
            double mib = bytes / (double)Mib;
            return mib.ToString("0.#") + " MiB";
        }
    }
}
