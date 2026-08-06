using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EditorTweaks.Features.DecorationSelection
{
    internal static class CameraRelativeDecorationDragPatches
    {
        private static readonly System.Reflection.FieldInfo DecorationPositionsAtDragStartField =
            AccessTools.Field(typeof(scnEditor), "decorationPositionsAtDragStart");

        private static readonly System.Reflection.FieldInfo AddXDragCacheField =
            AccessTools.Field(typeof(scnEditor), "addXDragCache");

        private static readonly System.Reflection.FieldInfo AddYDragCacheField =
            AccessTools.Field(typeof(scnEditor), "addYDragCache");

        [HarmonyPatch(typeof(scnEditor), "DragDecorationsStart")]
        private static class DragDecorationsStartPatch
        {
            private static void Postfix(scnEditor __instance)
            {
                if (!Main.Settings.EnableCameraRelativeDecorationDragFix)
                {
                    return;
                }

                Dictionary<scrDecoration, Vector2>? positions = GetDragStartPositions(__instance);
                if (positions == null)
                {
                    return;
                }

                foreach (ADOFAI.LevelEvent selectedDecoration in __instance.selectedDecorations)
                {
                    if (!IsCameraRelative(selectedDecoration))
                    {
                        continue;
                    }

                    scrDecoration decoration = scrDecorationManager.GetDecoration(selectedDecoration);
                    if (decoration != null)
                    {
                        positions[decoration] = (Vector2)selectedDecoration["position"];
                    }
                }
            }
        }

        [HarmonyPatch(typeof(scnEditor), nameof(scnEditor.DragDecorations))]
        private static class DragDecorationsPatch
        {
            private static bool Prefix(scnEditor __instance, Vector3 translation, bool ignoreModifiers = false)
            {
                if (!Main.Settings.EnableCameraRelativeDecorationDragFix || !HasCameraRelativeSelection(__instance))
                {
                    return true;
                }

                Dictionary<scrDecoration, Vector2>? positions = GetDragStartPositions(__instance);
                if (positions == null)
                {
                    return true;
                }

                bool preferX = Mathf.Abs(translation.x) + GetFloat(AddXDragCacheField, __instance) >
                    Mathf.Abs(translation.y) + GetFloat(AddYDragCacheField, __instance);
                AddXDragCacheField.SetValue(__instance, preferX ? 1f : 0f);
                AddYDragCacheField.SetValue(__instance, preferX ? 0f : 1f);

                foreach (ADOFAI.LevelEvent selectedDecoration in __instance.selectedDecorations)
                {
                    scrDecoration decoration = scrDecorationManager.GetDecoration(selectedDecoration);
                    if (decoration == null || selectedDecoration.locked || decoration.forceLock || !positions.ContainsKey(decoration))
                    {
                        continue;
                    }

                    DecPlacementType placementType = (DecPlacementType)selectedDecoration["relativeTo"];
                    if (placementType == DecPlacementType.Camera || placementType == DecPlacementType.CameraAspect)
                    {
                        DragCameraRelativeDecoration(__instance, selectedDecoration, decoration, positions[decoration], translation.xy(), preferX, ignoreModifiers);
                    }
                    else
                    {
                        DragRegularDecoration(__instance, selectedDecoration, decoration, positions[decoration], translation.xy(), preferX, ignoreModifiers);
                    }
                }

                if (__instance.SelectionDecorationIsSingle())
                {
                    __instance.levelEventsPanel.UpdatePropertyText(__instance.selectedDecorations[0], "position");
                }

                return false;
            }
        }

        private static void DragCameraRelativeDecoration(
            scnEditor editor,
            ADOFAI.LevelEvent selectedDecoration,
            scrDecoration decoration,
            Vector2 startPosition,
            Vector2 translation,
            bool preferX,
            bool ignoreModifiers)
        {
            DecPlacementType placementType = (DecPlacementType)selectedDecoration["relativeTo"];
            Vector2 dragDelta = GetCameraRelativeDragDelta(editor, placementType, translation);
            Vector2 newPosition = ApplyAxisLock(startPosition, startPosition + dragDelta, preferX, ignoreModifiers);

            selectedDecoration["position"] = newPosition;
            decoration.SetPosition(newPosition, decoration.pivotOffsetVec);
        }

        private static void DragRegularDecoration(
            scnEditor editor,
            ADOFAI.LevelEvent selectedDecoration,
            scrDecoration decoration,
            Vector2 startPosition,
            Vector2 translation,
            bool preferX,
            bool ignoreModifiers)
        {
            Vector2 dragDelta = editor.GetDecorationDragDelta(translation, decoration);
            Vector2 parallax = selectedDecoration.TryGet<Vector2>("parallax", out Vector2 parallaxValue)
                ? parallaxValue
                : Vector2.zero;
            if (parallax.x == 100f)
            {
                dragDelta.x = 0f;
            }

            if (parallax.y == 100f)
            {
                dragDelta.y = 0f;
            }

            Vector2 worldPosition = ApplyAxisLock(startPosition, startPosition + dragDelta, preferX, ignoreModifiers);
            Vector2 dataPosition = worldPosition;
            if ((DecPlacementType)selectedDecoration["relativeTo"] == DecPlacementType.Tile)
            {
                int index = Mathf.Clamp(selectedDecoration.floor, 0, editor.floors.Count - 1);
                dataPosition -= scrLevelMaker.instance.listFloors[index].transform.position.xy();
            }

            dataPosition /= ADOBase.controller.tileSize;
            selectedDecoration["position"] = dataPosition;
            decoration.SetPosition(worldPosition, decoration.pivotOffsetVec);
        }

        private static Vector2 ApplyAxisLock(Vector2 startPosition, Vector2 freePosition, bool preferX, bool ignoreModifiers)
        {
            if (!RDInput.holdingShift || ignoreModifiers)
            {
                return freePosition;
            }

            return new Vector2(preferX ? freePosition.x : startPosition.x, preferX ? startPosition.y : freePosition.y);
        }

        private static Vector2 GetCameraRelativeDragDelta(scnEditor editor, DecPlacementType placementType, Vector2 translation)
        {
            float screenUnitsPerWorldUnit = 20f / (editor.camera.orthographicSize * 2f);
            Vector2 delta = translation * screenUnitsPerWorldUnit;
            if (placementType == DecPlacementType.Camera)
            {
                delta.x /= editor.camera.aspect;
            }

            return delta;
        }

        private static bool HasCameraRelativeSelection(scnEditor editor)
        {
            foreach (ADOFAI.LevelEvent selectedDecoration in editor.selectedDecorations)
            {
                if (IsCameraRelative(selectedDecoration))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCameraRelative(ADOFAI.LevelEvent selectedDecoration)
        {
            if (selectedDecoration == null || selectedDecoration["relativeTo"] == null)
            {
                return false;
            }

            DecPlacementType placementType = (DecPlacementType)selectedDecoration["relativeTo"];
            return placementType == DecPlacementType.Camera || placementType == DecPlacementType.CameraAspect;
        }

        private static Dictionary<scrDecoration, Vector2>? GetDragStartPositions(scnEditor editor)
        {
            return DecorationPositionsAtDragStartField.GetValue(editor) as Dictionary<scrDecoration, Vector2>;
        }

        private static float GetFloat(System.Reflection.FieldInfo field, object instance)
        {
            return (float)field.GetValue(instance);
        }
    }
}
