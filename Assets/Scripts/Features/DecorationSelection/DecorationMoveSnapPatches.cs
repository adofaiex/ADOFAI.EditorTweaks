using HarmonyLib;
using UnityEngine;

namespace EditorTweaks.Features.DecorationSelection
{
    internal static class DecorationMoveSnapPatches
    {
        [HarmonyPatch(typeof(scnEditor), nameof(scnEditor.DragDecorations))]
        private static class DragDecorationsPatch
        {
            private static void Postfix(scnEditor __instance)
            {
                float step = Main.Settings.DecorationMoveSnapStep;
                if (step <= 0f || __instance == null || __instance.draggingGizmo != null)
                {
                    return;
                }

                bool changed = false;
                foreach (ADOFAI.LevelEvent selectedDecoration in __instance.selectedDecorations)
                {
                    if (SnapDecorationPosition(__instance, selectedDecoration, step))
                    {
                        changed = true;
                    }
                }

                if (changed && __instance.SelectionDecorationIsSingle())
                {
                    __instance.levelEventsPanel.UpdatePropertyText(__instance.selectedDecorations[0], "position");
                }
            }
        }

        private static bool SnapDecorationPosition(scnEditor editor, ADOFAI.LevelEvent selectedDecoration, float step)
        {
            if (selectedDecoration == null || selectedDecoration.locked || selectedDecoration["position"] == null)
            {
                return false;
            }

            scrDecoration decoration = scrDecorationManager.GetDecoration(selectedDecoration);
            if (decoration == null || decoration.forceLock)
            {
                return false;
            }

            Vector2 position = (Vector2)selectedDecoration["position"];
            Vector2 snappedPosition = new Vector2(Snap(position.x, step), Snap(position.y, step));
            if (Approximately(position, snappedPosition))
            {
                return false;
            }

            selectedDecoration["position"] = snappedPosition;
            decoration.SetPosition(ToDecorationPivotPosition(editor, selectedDecoration, snappedPosition), decoration.pivotOffsetVec);
            return true;
        }

        private static Vector2 ToDecorationPivotPosition(scnEditor editor, ADOFAI.LevelEvent selectedDecoration, Vector2 dataPosition)
        {
            DecPlacementType placementType = (DecPlacementType)selectedDecoration["relativeTo"];
            if (placementType == DecPlacementType.Camera || placementType == DecPlacementType.CameraAspect)
            {
                return dataPosition;
            }

            Vector2 pivotPosition = dataPosition * ADOBase.controller.tileSize;
            if (placementType == DecPlacementType.Tile)
            {
                int index = Mathf.Clamp(selectedDecoration.floor, 0, editor.floors.Count - 1);
                pivotPosition += scrLevelMaker.instance.listFloors[index].transform.position.xy();
            }

            return pivotPosition;
        }

        private static float Snap(float value, float step)
        {
            float snapped = Mathf.Round(value / step) * step;
            return Mathf.Abs(snapped) < 0.00001f ? 0f : snapped;
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Mathf.Abs(left.x - right.x) < 0.00001f && Mathf.Abs(left.y - right.y) < 0.00001f;
        }
    }
}
