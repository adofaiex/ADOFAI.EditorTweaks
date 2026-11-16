# 编辑器 Mod 架构

入口是 `ADOFAI.EditorTweaks.Main.Load`。启用时加载本地化和 UMM 设置，随后用本 Mod 的 Harmony ID 对整个程序集执行 `PatchAll`；停用时用同一个 ID 执行 `UnpatchAll`。

```text
UMM -> Main -> Settings / Localization
            -> Harmony.PatchAll
                 NumericDrag / DecorationSelection / VideoBackgroundSync
                 EditorPreferences / LevelLoading
```

`Src/` 每个文件夹是一个功能，补丁类以嵌套 `[HarmonyPatch]` 类的形式写在 `*Patches.cs` 里。视频背景补丁只处理普通游戏运行时的时间校正，不包含离线渲染分支。
