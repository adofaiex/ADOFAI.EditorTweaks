# 编辑器 Mod 架构

入口是 `ADOFAI.EditorTweaks.Main.Load`。启用时加载本地化和 UMM 设置，然后由独立的 PatchManager 按功能组注册 Harmony 补丁；停用时只撤销本项目自己的 Harmony ID。

本项目的运行链：

```text
UMM -> Main -> Settings / Localization -> PatchManager
                                      -> NumericDrag
                                      -> DecorationSelection
                                      -> VideoBackgroundSync
                                      -> EditorPreferences
                                      -> LevelLoading
```

视频背景补丁只处理普通游戏运行时的时间校正，不包含离线渲染分支。渲染、压缩包和 Web UI 位于另外两个独立项目。
