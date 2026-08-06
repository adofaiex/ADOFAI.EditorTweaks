using ADOFAI.Editor.Components;
using ADOFAI.LevelEditor.Controls;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EditorTweaks.Features.NumericDrag
{
    internal static class NumericDragPatches
    {
        [HarmonyPatch(typeof(DraggableNumberInputField), nameof(DraggableNumberInputField.OnPointerDown))]
        private static class DraggableNumberInputFieldOnPointerDownPatch
        {
            private static bool Prefix(DraggableNumberInputField __instance, PointerEventData eventData)
            {
                if (__instance.GetComponent<EditorTweaksNumericDragMarker>() == null)
                {
                    return true;
                }

                if (!EditorTweaksNumericDragMarker.IsDragButton(eventData))
                {
                    return false;
                }

                if (!float.TryParse(__instance.field.text, out float startValue))
                {
                    return false;
                }

                __instance.field.DeactivateInputField();
                AccessTools.Field(typeof(DraggableNumberInputField), "_startValue").SetValue(__instance, startValue);
                AccessTools.Field(typeof(DraggableNumberInputField), "_startPos").SetValue(__instance, eventData.position);
                AccessTools.Field(typeof(DraggableNumberInputField), "_isDragging").SetValue(__instance, false);
                AccessTools.Field(typeof(DraggableNumberInputField), "_down").SetValue(__instance, true);
                return false;
            }
        }

        [HarmonyPatch(typeof(DraggableNumberInputField), nameof(DraggableNumberInputField.OnPointerUp))]
        private static class DraggableNumberInputFieldOnPointerUpPatch
        {
            private static bool Prefix(DraggableNumberInputField __instance, PointerEventData eventData)
            {
                if (__instance.GetComponent<EditorTweaksNumericDragMarker>() == null)
                {
                    return true;
                }

                if (!EditorTweaksNumericDragMarker.IsDragButton(eventData))
                {
                    return false;
                }

                bool wasDragging = (bool)AccessTools.Field(typeof(DraggableNumberInputField), "_isDragging").GetValue(__instance);
                AccessTools.Field(typeof(DraggableNumberInputField), "_down").SetValue(__instance, false);
                AccessTools.Field(typeof(DraggableNumberInputField), "_isDragging").SetValue(__instance, false);
                if (wasDragging)
                {
                    __instance.GetComponent<EditorTweaksNumericDragMarker>()?.CommitAfterDrag();
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(DraggableNumberInputField), "SetArrowsVisible")]
        private static class DraggableNumberInputFieldSetArrowsVisiblePatch
        {
            private static bool Prefix(DraggableNumberInputField __instance)
            {
                return __instance.arrows != null;
            }
        }

        [HarmonyPatch(typeof(PropertyControl_Text), nameof(PropertyControl_Text.Setup))]
        private static class PropertyControlTextSetupPatch
        {
            private static void Postfix(PropertyControl_Text __instance)
            {
                NumericDragFeature.Attach(__instance);
            }
        }

        [HarmonyPatch(typeof(PropertyControl_Vector2), nameof(PropertyControl_Vector2.Setup))]
        private static class PropertyControlVector2SetupPatch
        {
            private static void Postfix(PropertyControl_Vector2 __instance)
            {
                NumericDragFeature.Attach(__instance);
            }
        }
    }
}
