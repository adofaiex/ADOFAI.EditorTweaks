using HarmonyLib;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    [HarmonyPatch(typeof(scrHitTextManager), nameof(scrHitTextManager.ShowHitText), typeof(HitMargin), typeof(scrPlanet), typeof(float))]
    internal static class ChartRenderJudgmentPatches
    {
        private static bool Prefix()
        {
            return !ChartRenderSession.IsRendering || ChartRenderService.ShowHitJudgments;
        }
    }
}
