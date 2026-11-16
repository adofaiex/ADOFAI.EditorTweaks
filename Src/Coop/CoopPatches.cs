#nullable disable
using HarmonyLib;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 告诉多人会话"谱面可能被改了"。编辑器里几乎所有修改都包在 SaveStateScope 里（撤销边界），
    /// 最外层结束时就是一次修改做完了；启用 / 隐藏 / 锁定事件不走撤销，单独补上。
    /// 拖动事件角度、拖装饰、变换手柄之类在松开鼠标时标记（见 CoopSession 的每帧检查）。
    /// 标记只是"可能变了"，真正是否发送由比较谱面 JSON 决定，所以多标几次没关系。
    /// </summary>
    [HarmonyPatch(typeof(SaveStateScope), nameof(SaveStateScope.Dispose))]
    internal static class CoopSaveStateScopePatch
    {
        private static void Postfix()
        {
            scnEditor editor = scnEditor.instance;
            if (editor != null && editor.changingState == 0) CoopSession.MarkDirty();
        }
    }

    [HarmonyPatch(typeof(scnEditor), nameof(scnEditor.EnableEvent))]
    internal static class CoopEnableEventPatch
    {
        private static void Postfix() => CoopSession.MarkDirty();
    }

    [HarmonyPatch(typeof(scnEditor), nameof(scnEditor.ShowEvent))]
    internal static class CoopShowEventPatch
    {
        private static void Postfix() => CoopSession.MarkDirty();
    }

    [HarmonyPatch(typeof(scnEditor), nameof(scnEditor.LockEvent))]
    internal static class CoopLockEventPatch
    {
        private static void Postfix() => CoopSession.MarkDirty();
    }
}
