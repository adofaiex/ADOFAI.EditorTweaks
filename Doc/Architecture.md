# 项目架构

ADOFAI Editor Tweaks 是一个 UnityModManager Mod，核心由按功能隔离的 Harmony Patch、少量常驻 MonoBehaviour 和独立功能服务组成。项目不注入自定义场景，不替换官方资源，尽量通过小范围接入修正编辑器和播放流程，并为谱面渲染与压缩包操作提供完整工作流。

技术选型、第三方版本和 Unity API 使用范围见 [TechnologyStack.md](TechnologyStack.md)。

## 加载流程

入口在 `src/Main.cs`：

```text
UnityModManager -> ADOFAI.EditorTweaks.Main.Load
```

`Load` 做以下初始化：

1. 保存 `UnityModManager.ModEntry` 到 `Main.Mod`。
2. 调用 `Localization.Load(modEntry)` 读取 `Resources/localization.json`。
3. 调用 `Settings.Load(modEntry)` 读取 UMM 设置。
4. 调用 `Settings.EnsureDefaults(modEntry)` 补全路径等默认值。
5. 注册：
   - `modEntry.OnToggle = OnToggle`
   - `modEntry.OnGUI = Settings.OnGUI`
   - `modEntry.OnSaveGUI = Settings.OnSaveGUI`
6. 首次使用时打开 `Resources/README.html` 用户手册。

启用 Mod 时：

- `PatchManager.ApplyAll(modEntry.Info.Id)` 按功能组同步应用补丁。
- 只有 `EditorOverlayInputGuard` 可用时才调用 `EditorTweaksOverlayWindow.Ensure()`。
- 单个功能组失败不会阻止其他组启用。

禁用 Mod 时：

- `EditorTweaksOverlayWindow.Destroy()`
- `PatchManager.UnpatchAll()` 清理所有已启用功能组。

## PatchManager 生命周期

`src/Patching/PatchManager.cs` 显式定义 10 个功能组。每组使用独立的 Harmony ID，按补丁类型逐个应用；任意类型失败时回滚整个组并继续下一组。

补丁状态分为：

- `Active`：组内补丁全部应用成功。
- `Failed`：注册、目标解析、初始化或应用失败，组内没有保留半生效补丁。
- `Blocked`：依赖组不可用，因此未尝试应用。
- `Inactive`：Mod 未启用或已经停用。

`ChartRendering` 依赖 `EditorOverlayInputGuard`。`ArchiveIo` 在 Harmony Prepare 阶段验证压缩组件，失败时回滚压缩包组并保留游戏原有压缩方法。

启动扫描会验证全部 `[HarmonyPatch]` 类型恰好属于一个组。完整清单和分组见 [PatchInventory.md](PatchInventory.md)。

## 模块边界

`src/Features` 下每个目录代表一个相对独立的功能域：

- `ChartRendering`：谱面视频渲染。负责播放启动、定帧、自动打击、画面捕获、音频捕获、编码、日志。
- `ArchiveIo`：常见压缩包解压、ADOZIP 导出、旧 ZIP 文件名识别和路径安全校验。
- `CloudSettings`：Steam 云设置的手动上传和下载。
- `DecorationSelection`：装饰选择、拖动、轴心和吸附修复。
- `EditorOverlay`：编辑器内浮窗和输入遮罩。
- `EditorPreferences`：官方偏好设置即时保存。
- `LevelLoading`：合并游戏重复登记的缺图错误，避免关卡加载流程中断。
- `NumericDrag`：数值输入框拖动。
- `VideoBackgroundSync`：视频背景时间校正。

公共基础：

- `Api/Rendering`：稳定的公共请求、任务、进度、结果和枚举；不暴露 Unity 或编码内部类型。
- `Patching/PatchManager.cs`：补丁分组、依赖、兼容状态和整组回滚。
- `Settings.cs`：UMM 设置对象、设置 UI、默认值、渲染参数范围校验。
- `Localization.cs`：JSON 本地化加载和语言选择。
- `Resources/localization.json`：用户可见文本。
- `ADOFAIMod.targets`：构建后复制、FFmpeg 下载、部署到游戏目录、生成 Build 产物和 zip。
- `scripts/EnsureFfmpeg.ps1`：缺失时下载并校验 FFmpeg。
- `scripts/BumpModVersion.ps1`：发行构建时递增 `Info.json` 版本号。
- `scripts/PackageMod.ps1`：把 `out/` 打包成 `Build/<ModId>-<Version>/` 和 zip。

## 状态管理

Mod 的状态主要来自三个地方：

- `Main.Settings`：用户配置。
- Harmony Patch 的静态状态：例如视频同步状态、渲染诊断状态。
- `PatchManager.Statuses`：当前启用周期中每个功能组的兼容状态。
- 运行期对象：例如 `EditorTweaksOverlayWindow` 和一次性的 `ChartRenderSession`。

渲染器有一个全局静态标记：

```text
ChartRenderSession.IsRendering
```

这个标记被多个模块使用：

- `ChartRenderVisualClock` 判断是否强制 conductor 视觉时间。
- `ChartRenderAutoPlayer` 判断是否自动补打。
- `ChartRenderAudioPatches` 判断是否屏蔽界面音。
- `ChartRenderJudgmentPatches` 判断是否隐藏判定文字。
- `EditorOverlayInputBlockPatches` 判断是否启用模态输入遮罩。

维护时要注意：`IsRendering` 的生命周期必须覆盖从播放启动到最终清理的整个过程，且失败、取消、FFmpeg 后台线程错误都要能走到 `Finish()` 或 `Cleanup()`。

画面捕获使用 `IChartFrameCapture` 抽象：

- `ChartCameraFrameCapture` 输出独立于窗口大小的摄像机画面。
- `ChartGameViewFrameCapture` 输出 Unity 帧末最终游戏画面，尺寸固定为任务开始时的游戏分辨率。
- `ChartFrameCaptureFactory` 根据本次任务锁定的 `ChartRenderCaptureSource` 创建后端。

两个后端共用 `ChartRenderFramePipeline`、音频捕获和编码流程。详细生命周期见 [ChartRendering.md](ChartRendering.md)。

`ChartRenderService` 是唯一任务所有者和协程宿主。内置浮窗与其他 Mod 都通过 `ChartRenderApi` 创建请求；Service 把公共请求复制为内部配置，再创建 `ChartRenderSession`。公共 API 因此不依赖浮窗是否显示，也不会把调用方的单次设置写回玩家配置。接口参考见 [Api/ChartRendering.md](Api/ChartRendering.md)。

## 设置与本地化

设置对象继承 `UnityModManager.ModSettings`。UMM UI 直接写 `Main.Settings` 字段，并在重要设置变化时调用 `Save(modEntry)`。

渲染设置分为：

- 基础设置：玩家常用，默认显示。
- 高级设置：排查和特殊导出用，默认隐藏。

`Settings.NormalizeChartRenderSettings()` 会在保存前规范化：

- 宽高限制在安全范围并变成偶数。
- FPS 限制在 1 到 240。
- CRF 限制在 0 到 51。
- preset 空值回到 `veryfast`。
- 编码档位、回读格式、预览模式非法时回到默认值。
- 画面捕获方式非法时回到 `camera`。
- 旧 ZIP 文件名编码非法时回到 `Auto`。
- 结尾尾巴秒数不允许小于 0。
- 音频同步偏移限制在 -5000 到 5000 毫秒。

## 构建和部署

项目目标框架是 `net481`。`ADOFAI.EditorTweaks.csproj` 通过 `GameExePath` 推导游戏 managed assemblies 路径，并引用 `Assembly-CSharp.dll`、UnityEngine 模块、UMM、Harmony 等 DLL。

构建命令：

```powershell
dotnet build
```

推荐脚本：

```bat
build-dev.bat
build-release.bat
build-release.bat Patch
```

`ADOFAIMod.targets` 做：

1. `ValidateGameExePath`：游戏 exe 不存在则构建失败。
2. `EnsureFfmpegTool`：Windows 下如果 `tools/ffmpeg.exe` 缺失，调用 `scripts/EnsureFfmpeg.ps1` 下载。
3. `BumpInfoJsonVersion`：仅在 `BumpModVersion=true` 时递增版本号。
4. `CopyToOut`：清空并重建 `out/`，复制 DLL、`Info.json`、`Resources`、`Tools`、`ThirdParty`。
5. `PackageMod`：生成 `Build/<ModId>-<Version>/` 和 `Build/<ModId>-<Version>.zip`。
6. `DeployAndLaunch`：部署到游戏的 `Mods/ADOFAI.EditorTweaks/`。
7. `AutoLaunchGame=true` 时才启动游戏。

## 维护原则

- Patch 尽量小，优先在 Prefix/Postfix 中短路特定情况。
- 需要访问私有字段时，用 `AccessTools.Field`，并在文档中写清楚字段名和用途。
- 避免在渲染期间暂停或跳过核心游戏 Update，除非确认不会影响画面推进。
- 修改渲染器时要做取消、失败、成功三条路径的状态恢复检查。
- 新增补丁必须注册到且只注册到一个 PatchManager 功能组。
- 修改压缩包处理时必须同时验证路径穿越、重复条目、覆盖保护、条目数量和总解压大小。
- 用户可见行为变化必须同步更新 `Resources/README.html`、模块文档和本地化说明。
