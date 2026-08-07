using HarmonyLib;
using UnityEngine.EventSystems;
using ADOFAI.EditorTweaks.Features.ChartRendering;

namespace ADOFAI.EditorTweaks.Features.RenderInputGuard
{
    internal static class RenderInputGuardPatches
    {
        [HarmonyPatch(typeof(scnEditor), "Update")]
        internal static class EditorUpdatePatch
        {
            private static bool Prefix()
            {
                return !ChartRenderService.IsActive;
            }
        }

        [HarmonyPatch(typeof(scnEditor), "ZoomCamera")]
        internal static class EditorZoomCameraPatch
        {
            private static bool Prefix()
            {
                return !ChartRenderService.IsActive;
            }
        }

        [HarmonyPatch(typeof(scrController), "Update")]
        internal static class ControllerUpdatePatch
        {
            private static bool Prefix()
            {
                // Keep the controller's timeline update alive; only input-producing methods below are guarded.
                return true;
            }
        }

        [HarmonyPatch(typeof(scrController), nameof(scrController.TogglePauseGame))]
        internal static class ControllerTogglePausePatch
        {
            private static bool Prefix(ref bool __result, scrController __instance)
            {
                if (!ChartRenderService.IsActive)
                {
                    return true;
                }

                __result = __instance.paused;
                return false;
            }
        }

        [HarmonyPatch(typeof(scrPlayerManager), nameof(scrPlayerManager.AnyValidInputWasTriggered))]
        internal static class PlayerManagerInputPatch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!ChartRenderService.IsActive)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.ValidInputWasTriggered))]
        internal static class PlayerInputTriggeredPatch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!ChartRenderService.IsActive)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.ValidInputWasReleased))]
        internal static class PlayerInputReleasedPatch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!ChartRenderService.IsActive)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.CountValidKeysPressed))]
        internal static class PlayerInputCountPatch
        {
            private static bool Prefix(ref int __result)
            {
                if (!ChartRenderService.IsActive)
                {
                    return true;
                }

                __result = 0;
                return false;
            }
        }

        [HarmonyPatch(typeof(StandaloneInputModule), nameof(StandaloneInputModule.Process))]
        internal static class StandaloneInputModulePatch
        {
            private static bool Prefix()
            {
                return !ChartRenderService.IsActive;
            }
        }
    }
}
