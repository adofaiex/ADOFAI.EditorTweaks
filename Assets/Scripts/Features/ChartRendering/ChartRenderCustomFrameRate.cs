using HarmonyLib;

namespace EditorTweaks.Features.ChartRendering
{
    internal static class ChartRenderCustomFrameRate
    {
        private static bool active;
        private static bool wasEnabled;
        private static bool forceNextCapture;
        private static int refreshCount;
        private static int consumedRefreshCount;
        private static int repeatedFrames;

        public static void Begin()
        {
            active = true;
            wasEnabled = false;
            forceNextCapture = true;
            refreshCount = 0;
            consumedRefreshCount = 0;
            repeatedFrames = 0;
        }

        public static void End()
        {
            if (active && repeatedFrames > 0)
            {
                ChartRenderDiagnostics.Log("Custom frame rate repeated frames=" + repeatedFrames + ".");
            }

            active = false;
            wasEnabled = false;
            forceNextCapture = false;
            refreshCount = 0;
            consumedRefreshCount = 0;
            repeatedFrames = 0;
        }

        public static void RecordScreenRefresh()
        {
            if (!active || !ChartRenderSession.IsRendering)
            {
                return;
            }

            refreshCount++;
        }

        public static bool ShouldCaptureFrame()
        {
            if (!active || !ChartRenderSession.IsRendering)
            {
                return true;
            }

            scrCamera? camera = scrCamera.instance;
            float frameRate = camera == null ? 0f : camera.frameRate;
            bool enabled = camera != null && camera.enableCustomFPS && frameRate > 0.001f;
            if (!enabled)
            {
                if (wasEnabled)
                {
                    ChartRenderDiagnostics.Log("Custom frame rate disabled.");
                }

                wasEnabled = false;
                forceNextCapture = false;
                return true;
            }

            if (!wasEnabled)
            {
                ChartRenderDiagnostics.Log("Custom frame rate enabled. frameRate=" + frameRate + ".");
                wasEnabled = true;
                forceNextCapture = true;
            }

            if (forceNextCapture)
            {
                forceNextCapture = false;
                consumedRefreshCount = refreshCount;
                return true;
            }

            if (refreshCount > consumedRefreshCount)
            {
                consumedRefreshCount = refreshCount;
                return true;
            }

            repeatedFrames++;
            return false;
        }
    }

    [HarmonyPatch(typeof(scrCamera), "UpdateCustomFrameRateScreen")]
    internal static class ChartRenderCustomFrameRateScreenPatch
    {
        private static void Prefix()
        {
            ChartRenderCustomFrameRate.RecordScreenRefresh();
        }
    }
}
