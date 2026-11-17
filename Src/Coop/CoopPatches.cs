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

    /// <summary>
    /// "专业事件"是每个玩家自己的偏好：没开的人读谱时会把专业属性直接丢掉。房主开了、客人没开，
    /// 客人读出来的谱就和房主的不一样（校验永远对不上，一直重新同步），客人改了这些事件还会把专业属性抹掉。
    /// 所以在多人房间里一律按"开"来读写谱面（只影响读写，不改玩家的设置，也不改检查器里显示什么）。
    /// </summary>
    [HarmonyPatch(typeof(ADOFAI.LevelEvent), nameof(ADOFAI.LevelEvent.enableProEvents), MethodType.Getter)]
    internal static class CoopProEventsPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && CoopSession.InSession) __result = true;
        }
    }
}
