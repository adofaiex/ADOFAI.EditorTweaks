using HarmonyLib;

namespace EditorTweaks.Features.ChartRendering
{
    internal static class ChartRenderVisualClock
    {
        private static double startSongPosition;
        private static double forcedSongPosition;

        public static bool IsActive { get; private set; }

        public static void Begin(double songPosition)
        {
            startSongPosition = songPosition;
            forcedSongPosition = songPosition;
            IsActive = true;
        }

        public static void End()
        {
            IsActive = false;
            startSongPosition = 0.0;
            forcedSongPosition = 0.0;
        }

        public static void SetFrameTime(double seconds, float pitch)
        {
            forcedSongPosition = startSongPosition + seconds * pitch;
        }

        public static bool TryGetSongPosition(out double songPosition)
        {
            songPosition = forcedSongPosition;
            return IsActive && ChartRenderSession.IsRendering;
        }
    }

    [HarmonyPatch(typeof(scrConductor), "set_songposition_minusi")]
    internal static class ChartRenderConductorSongPositionPatch
    {
        private static void Prefix(ref double value)
        {
            if (ChartRenderVisualClock.TryGetSongPosition(out double songPosition))
            {
                value = songPosition;
            }
        }
    }

    [HarmonyPatch(typeof(scrConductor), "get_songposition_minusi")]
    internal static class ChartRenderConductorSongPositionGetterPatch
    {
        private static void Postfix(ref double __result)
        {
            if (ChartRenderVisualClock.TryGetSongPosition(out double songPosition))
            {
                __result = songPosition;
            }
        }
    }
}
