using ADOFAI;
using HarmonyLib;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.DecorationSelection
{
    internal static class DecorationPivotPatches
    {
        [HarmonyPatch(typeof(DecorationPivot), nameof(DecorationPivot.UpdatePivotCrossImage))]
        private static class DecorationPivotUpdatePivotCrossImagePatch
        {
            private static bool Prefix(DecorationPivot __instance, bool enable)
            {
                if (!Main.Settings.EnableDecorationPivotFix)
                {
                    return true;
                }

                bool hide = ADOBase.editor.SelectionDecorationIsEmpty() || !ADOBase.editor.SelectionDecorationIsSingle();
                if (!hide)
                {
                    scrDecoration decoration = scrDecorationManager.GetDecoration(ADOBase.editor.selectedDecorations[0]);
                    if (decoration != null)
                    {
                        SetPivotCrossPosition(__instance, decoration);
                    }
                }

                if (__instance.gizmoTransform != null)
                {
                    __instance.gizmoTransform.gameObject.SetActive(enable && !hide);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(scrDecoration), "UpdateScreenClamp")]
        private static class ScrDecorationUpdateScreenClampPatch
        {
            private static void Postfix(scrDecoration __instance)
            {
                if (!Main.Settings.EnableDecorationPivotFix || __instance == null || __instance.parallax == null)
                {
                    return;
                }

                if (!IsScreenRelative(__instance.placementType))
                {
                    return;
                }

                // 只在编辑器环境下修正，不影响游戏播放
                if (ADOBase.editor == null || !ADOBase.isEditingLevel)
                {
                    return;
                }

                // 只对当前选中的装饰修正位置，不影响所有装饰
                if (ADOBase.editor.selectedDecorations == null 
                    || ADOBase.editor.selectedDecorations.Count == 0
                    || __instance.sourceLevelEvent == null
                    || !ADOBase.editor.selectedDecorations.Contains(__instance.sourceLevelEvent))
                {
                    return;
                }

                // The game keeps the logical pivot and the image offset separately.
                // UpdateScreenClamp uses their sum as the screen-relative position;
                // dropping pivotOffsetVec here makes a decoration jump after its
                // pivot offset is edited and then dragged.
                Vector2 pivot = __instance.pivotPosVec + __instance.pivotOffsetVec;
                if (__instance.placementType == DecPlacementType.CameraAspect && Screen.width != 0)
                {
                    pivot.x *= (float)Screen.height / Screen.width;
                }

                __instance.parallax.screenRelativePos = pivot / 20f + new Vector2(0.5f, 0.5f);
            }
        }

        [HarmonyPatch(typeof(scrParallax), nameof(scrParallax.SetTrans))]
        private static class ScrParallaxSetTransPatch
        {
            private static void Postfix(scrParallax __instance)
            {
                if (!Main.Settings.EnableDecorationPivotFix || __instance == null || __instance.decoration == null)
                {
                    return;
                }

                scnEditor editor = ADOBase.editor;
                if (editor == null
                    || editor.decPivot == null
                    || !editor.SelectionDecorationIsSingle()
                    || editor.selectedDecorations[0] != __instance.decoration.sourceLevelEvent)
                {
                    return;
                }

                SetPivotCrossRenderedPosition(editor.decPivot, __instance.decoration);
            }
        }

        private static bool IsScreenRelative(DecPlacementType placementType)
        {
            return placementType == DecPlacementType.Camera || placementType == DecPlacementType.CameraAspect;
        }

        private static void SetPivotCrossPosition(DecorationPivot pivot, scrDecoration decoration)
        {
            if (pivot.gizmoTransform == null)
            {
                return;
            }

            // Use the same logical pivot coordinate as the game's own
            // UpdatePivotCrossImage implementation. transform.position also
            // includes parallax/image offsets and is not the decoration pivot.
            pivot.gizmoTransform.transform.position = decoration.pivotPosVec;
        }

        private static void SetPivotCrossRenderedPosition(DecorationPivot pivot, scrDecoration decoration)
        {
            if (pivot.gizmoTransform == null)
            {
                return;
            }

            // SetTrans runs after the camera/parallax transform has been applied.
            // At this point the gizmo must follow the rendered world position;
            // using pivotPosVec here would reset it to the untransformed editor
            // coordinate on every frame.
            pivot.gizmoTransform.transform.position = decoration.transform.position;
        }
    }
}
