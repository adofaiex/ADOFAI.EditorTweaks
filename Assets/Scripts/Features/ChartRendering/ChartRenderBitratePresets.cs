using System;

namespace EditorTweaks.Features.ChartRendering
{
    internal static class ChartRenderBitratePresets
    {
        public const int AutoBitrateMbps = 0;
        public const int MaxBitrateMbps = 300;

        public static int ResolveTargetBitrateMbps(int configuredBitrateMbps, int width, int height, int fps)
        {
            return configuredBitrateMbps <= AutoBitrateMbps
                ? GetRecommendedBitrateMbps(width, height, fps)
                : Math.Max(1, Math.Min(MaxBitrateMbps, configuredBitrateMbps));
        }

        public static int GetRecommendedBitrateMbps(int width, int height, int fps)
        {
            long pixels = Math.Max(1L, (long)Math.Max(1, width) * Math.Max(1, height));
            int baseBitrate;
            if (pixels <= 1280L * 720L)
            {
                baseBitrate = 8;
            }
            else if (pixels <= 1920L * 1080L)
            {
                baseBitrate = 20;
            }
            else if (pixels <= 2560L * 1440L)
            {
                baseBitrate = 35;
            }
            else if (pixels <= 3840L * 2160L)
            {
                baseBitrate = 60;
            }
            else
            {
                baseBitrate = 120;
            }

            double fpsScale = Math.Sqrt(Math.Max(1, fps) / 60.0);
            int recommended = (int)Math.Round(baseBitrate * fpsScale);
            return Math.Max(4, Math.Min(MaxBitrateMbps, recommended));
        }

        public static int GetMaxRateMbps(int targetBitrateMbps)
        {
            return Math.Max(targetBitrateMbps, (int)Math.Ceiling(targetBitrateMbps * 1.5));
        }

        public static int GetBufferSizeMbps(int targetBitrateMbps)
        {
            return Math.Max(targetBitrateMbps * 2, GetMaxRateMbps(targetBitrateMbps));
        }
    }
}
