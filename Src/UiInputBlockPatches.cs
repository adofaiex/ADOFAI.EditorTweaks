using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ADOFAI.EditorTweaks
{
    /// <summary>
    /// UI Toolkit 的界面（右侧面板、设置窗口）挡不住游戏自己读鼠标：编辑器用 EventSystem.IsPointerOverGameObject
    /// 判断鼠标是不是在界面上，不是就当成在谱面上点击 / 拖动。这里让它把我们的界面也算进去，
    /// 鼠标在面板上点按钮时就不会顺带放砖块、取消选择；滚轮在面板上也不会缩放谱面。
    /// </summary>
    [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.IsPointerOverGameObject), new System.Type[0])]
    internal static class PointerOverUiPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && SettingsUi.IsPointerOverUi()) __result = true;
        }
    }

    [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.IsPointerOverGameObject), typeof(int))]
    internal static class PointerOverUiWithIdPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && SettingsUi.IsPointerOverUi()) __result = true;
        }
    }

    /// <summary>在我们的输入框里打字时，告诉编辑器"用户正在输入框里编辑"，它就不会把按键当快捷键（Delete 删砖、空格播放之类）。</summary>
    [HarmonyPatch(typeof(scnEditor), nameof(scnEditor.userIsEditingAnInputField), MethodType.Getter)]
    internal static class TypingInOurUiPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && SettingsUi.IsTyping()) __result = true;
        }
    }

    [HarmonyPatch(typeof(scnEditor), nameof(scnEditor.ZoomCamera))]
    internal static class ZoomOverUiPatch
    {
        private static bool Prefix()
        {
            return Input.mouseScrollDelta == Vector2.zero || !SettingsUi.IsPointerOverUi();
        }
    }
}
