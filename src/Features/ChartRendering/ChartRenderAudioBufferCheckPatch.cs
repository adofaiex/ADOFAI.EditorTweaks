using HarmonyLib;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    [HarmonyPatch(typeof(scnLevelSelect), "CheckAudioBreak")]
    internal static class ChartRenderAudioBufferCheckPatch
    {
        private static bool Prefix()
        {
            return !ChartRenderSession.IsRendering;
        }
    }
}
