using HarmonyLib;
using UnityEngine;

namespace EditorTweaks.Features.ChartRendering
{
    [HarmonyPatch(typeof(scrSfx), nameof(scrSfx.PlaySfx), typeof(AudioClip), typeof(MixerGroup), typeof(float), typeof(float), typeof(float))]
    internal static class ChartRenderAudioPatches
    {
        private static bool Prefix(AudioClip clip, MixerGroup group, ref AudioClip __result)
        {
            if (!ChartRenderSession.IsRendering || group != MixerGroup.InterfaceParent)
            {
                return true;
            }

            __result = clip;
            return false;
        }
    }
}
