# 模块文档索引

这个目录记录 ADOFAI Editor Tweaks 的开发、维护和排错资料。根目录 [README.md](../README.md) 负责项目总览；面向玩家的纯操作说明位于 [Resources/README.html](../Resources/README.html)。

## 总览

- [Architecture.md](Architecture.md)：项目架构、加载流程、构建部署、状态管理。
- [TechnologyStack.md](TechnologyStack.md)：语言、运行时、Unity 接口、渲染、压缩包、构建链和第三方依赖。
- [PatchInventory.md](PatchInventory.md)：所有 Harmony Patch 的目标方法、类型、作用和风险点。
- [SettingsAndLocalization.md](SettingsAndLocalization.md)：UMM 设置、本地化、默认值和 UI 设计。
- [BuildAndRelease.md](BuildAndRelease.md)：开发构建、正式发行、自动升版本、Build 产物和 zip 打包。
- [Resources/README.html](../Resources/README.html)：面向玩家的安装、编辑、压缩包和渲染操作手册。
- [../CHANGELOG.md](../CHANGELOG.md)：版本更新日志。

## Features

- [ChartRendering.md](ChartRendering.md)：离线谱面视频渲染、定帧、视觉时钟、自动打击、音频捕获、FFmpeg、诊断日志。
- [Api/README.md](Api/README.md)：公共 API 文档索引。
- [Api/ChartRendering.md](Api/ChartRendering.md)：供其他 Mod 调用的强类型渲染任务 API、生命周期、错误码和示例。
- [PatchInventory.md#archiveio](PatchInventory.md#archiveio)：常见压缩包接管、旧 ZIP 文件名识别和失败隔离。
- [DecorationSelection.md](DecorationSelection.md)：Camera / CameraAspect 装饰拖动、轴心、吸附。
- [EditorOverlay.md](EditorOverlay.md)：编辑器浮窗、渲染进度窗、输入遮罩。
- [EditorPreferences.md](EditorPreferences.md)：官方编辑器偏好即时保存。
- [NumericDrag.md](NumericDrag.md)：数值输入框右键拖动。
- [VideoBackgroundSync.md](VideoBackgroundSync.md)：视频背景中途播放同步修复。

## 维护约定

- 新增 Harmony Patch 时同步更新 [PatchInventory.md](PatchInventory.md)。
- 修改 `src/Features/<FeatureName>` 时同步更新对应模块文档。
- 修改渲染器时优先更新 [ChartRendering.md](ChartRendering.md)，因为渲染功能牵涉视觉、音频、输入、FFmpeg、UI 和日志。
- 技术依赖、运行时接口或第三方版本变化同步更新 [TechnologyStack.md](TechnologyStack.md)。
- 面向玩家的功能和操作变化同步更新 [Resources/README.html](../Resources/README.html)；不要在用户手册中解释内部实现。
- 设置字段和本地化变化同步更新 [SettingsAndLocalization.md](SettingsAndLocalization.md)。
- 修改构建、打包、版本号或发行脚本时同步更新 [BuildAndRelease.md](BuildAndRelease.md) 和 [../CHANGELOG.md](../CHANGELOG.md)。
