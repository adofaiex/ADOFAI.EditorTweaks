# ADOFAI.EditorTweaks

这是编辑器与游戏优化 Mod，版本 `1.4.8`。它与 `ADOFAI.EditorTweaks.BetterZip`、`ADOFAI.EditorTweaks.ChartRendering` 完全独立，不依赖对方的 DLL、源码或配置。

包含功能：

- 数值输入框拖动调节，并可设置步长与小数位。
- Camera / CameraAspect 装饰拖动、移动吸附和轴心显示修复。
- 普通游戏运行时的视频背景同步。
- 编辑器偏好即时保存。
- 缺图错误去重。
- UMM 设置面板。

本项目不包含谱面视频渲染、FFmpeg、Web UI、压缩包处理或额外设置同步。

## 构建

```powershell
dotnet build ADOFAI.EditorTweaks.csproj -c Debug
dotnet build ADOFAI.EditorTweaks.csproj -c Release
```

也可以使用 `build-dev.bat` 和 `build-release.bat`。输出在 `out/`，发行压缩包在 `Build/`，部署目录为：

```text
Mods/ADOFAI.EditorTweaks/
```

构建从本机游戏目录读取 Managed DLL，不会把游戏 DLL 复制到 Mod 包。

## 文档

- [用户手册](Doc/README.md)
- [架构](Doc/Architecture.md)
- [构建说明](Doc/BuildAndRelease.md)
- [补丁清单](Doc/PatchInventory.md)
- [设置与本地化](Doc/SettingsAndLocalization.md)
- [技术栈](Doc/TechnologyStack.md)
- [功能文档](Doc/NumericDrag.md)、[装饰选择](Doc/DecorationSelection.md)、[视频背景同步](Doc/VideoBackgroundSync.md)、[编辑器偏好](Doc/EditorPreferences.md)
