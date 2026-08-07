# 技术栈与运行时依赖

本文面向维护者，记录 ADOFAI Editor Tweaks 1.4.5 实际使用的语言、运行时、第三方组件、游戏接口和构建链。玩家操作请阅读发布包中的 `Resources/README.html`。

## 技术栈总览

| 层次 | 技术 | 当前用途 |
| --- | --- | --- |
| 语言 | C#，`LangVersion=latest`，Nullable 开启 | Mod 主体、功能服务、运行时 UI 和 Harmony 补丁。 |
| 目标框架 | .NET Framework 4.8.1（`net481`） | 与当前 ADOFAI/UnityModManager 托管环境兼容。 |
| 游戏引擎 | Unity 6 | 场景、摄像机、最终画面捕获、音频捕获、协程和 GPU 回读。 |
| Mod 加载 | UnityModManager 0.27.0+ | 加载入口、启停回调、设置持久化、设置面板和日志。 |
| 方法补丁 | Harmony 2（`0Harmony.dll`） | 在不替换游戏程序集的前提下接入编辑器和播放流程。 |
| 游戏程序集 | `Assembly-CSharp.dll`、`Assembly-CSharp-firstpass.dll` | 访问 ADOFAI 的编辑器、控制器、谱面、视频和压缩包入口。 |
| UI | React、Vite、TypeScript、Arco Design、Unity IMGUI | 外部 Web 设置页、UMM 快捷键录入以及渲染期间输入保护。 |
| 本地通信 | `HttpListener`、HTTP、SSE | 本机 Web UI 与 Unity 主线程之间的设置、操作和状态同步。 |
| 视频输出 | FFmpeg 8.1.2 essentials build | 将原始画面和 WAV 合成为 MP4、MKV 或 MOV。 |
| 压缩包 | SharpSevenZip 2.0.109 + x64 `7z.dll` | 读取常见压缩格式并创建兼容原版的 ZIP/ADOZIP。 |
| 云存储 | 游戏 Steam 集成、Facepunch.Steamworks | 手动上传和下载 Mod 设置。 |
| 构建 | .NET SDK/MSBuild、PowerShell | 同步游戏引用、下载依赖、部署、打包和版本校验。 |
| 自动发行 | GitHub Actions、GitHub Release | 根据版本标签构建并发布 Release 产物。 |

公共集成层使用普通 .NET 强类型 API，位于 `ADOFAI.EditorTweaks.Api.Rendering`。调用方只接触请求、任务、进度、结果和显式编号枚举，不直接依赖 Unity 对象、UMM 对象、Harmony、RenderTexture 或 FFmpeg。

## 运行时宿主

项目编译为 `ADOFAI.EditorTweaks.dll`，入口由 `Info.json` 指向：

```text
ADOFAI.EditorTweaks.Main.Load
```

`Main.Load` 只负责不依赖功能补丁的基础初始化：

1. 保存 `UnityModManager.ModEntry`。
2. 加载 `Resources/localization.json`。
3. 加载设置并补齐新字段默认值。
4. 注册 `OnToggle`、`OnGUI` 和 `OnSaveGUI`。
5. 首次启用时打开本地用户手册。

启用 Mod 时，`PatchManager.ApplyAll(modId)` 同步应用各功能组并启动 `WebUiHost`。停用时先停止本地 Web 服务和渲染任务，再调用 `PatchManager.UnpatchAll()`。HTTP 线程不直接修改 Unity 状态。

## Harmony 与功能隔离

`src/Patching/PatchManager.cs` 将补丁划分为 10 个功能组：

1. Numeric Drag
2. Camera Relative Decoration Drag
3. Decoration Move Snap
4. Decoration Pivot
5. Video Background Sync
6. Editor Preferences
7. Render Input Guard
8. Chart Rendering
9. Archive I/O
10. Image Load Error Deduplication

每组拥有独立 Harmony ID，并通过 `CreateClassProcessor(type).Patch()` 逐类型应用。组内任意补丁失败时会清除该组已经应用的全部补丁，记录失败类型和完整异常，然后继续加载其他组。

`Chart Rendering` 依赖 `Render Input Guard`。输入保护不可用时，渲染组会标记为依赖阻止，避免进入无法安全拦截输入的渲染状态。

启动时会扫描当前程序集中的 `[HarmonyPatch]` 类型，确认每个类型恰好注册到一个功能组。遗漏和重复注册都会进入错误日志，不允许新补丁静默失效。

## Unity 侧接口

项目直接引用游戏安装目录中的托管程序集，而不是复制官方源码。主要 Unity 能力如下：

| 能力 | 使用位置 |
| --- | --- |
| `MonoBehaviour`、协程、`WaitForEndOfFrame` | Web 服务宿主和离线渲染主流程。 |
| `Camera.targetTexture`、`RenderTexture` | 摄像机渲染后端。 |
| `ScreenCapture.CaptureScreenshotIntoRenderTexture` | 游戏画面渲染后端，捕获帧末最终画面。 |
| `AsyncGPUReadback` | 异步把画面纹理读取到编码缓冲区。 |
| `AudioRenderer` | 在定帧渲染期间捕获游戏混音并写入浮点 WAV。 |
| `Time.captureFramerate` | 固定游戏视觉时间步长。 |
| Unity IMGUI | UMM 快捷键录入。 |
| Unity Input / UI 模块 | 渲染期间的输入保护。 |
| `VideoPlayer` | 带视频背景谱面的启动同步。 |

项目同时引用 DOTween、RDTools、TextMeshPro、Unity UI、Unity Collections 和游戏使用的其他 Unity 模块，以匹配官方类型签名并访问现有对象。

## 离线谱面渲染栈

渲染流程由 `ChartRenderSession` 组织，核心组件如下：

```text
ChartRenderSession
├── ChartRenderPlaybackController     启动整首或选中段落播放
├── ChartRenderVisualClock            提供固定输出帧对应的谱面时间
├── IChartFrameCapture
│   ├── ChartCameraFrameCapture       摄像机链画面
│   └── ChartGameViewFrameCapture     帧末最终游戏画面
├── ChartRenderFramePipeline          回读排序、重复帧和编码队列
├── ChartRenderMemoryBudget           按分辨率约束内存和并发帧
├── ChartUnityAudioCapture            离线音频捕获
└── FfmpegEncoder                     视频编码和音画合成
```

`ChartRenderService` 位于会话之上，负责全局单任务互斥、主线程协程和公共任务状态。Web 设置页调用相同的 `ChartRenderApi`，避免产生第二套启动与清理逻辑。

### 摄像机后端

摄像机模式把游戏的 `Bgcamstatic`、`BGcam` 和 `camobj` 输出到同一个 `ARGB32` RenderTexture。它不依赖游戏窗口分辨率，可以生成自定义大小的纯净谱面画面。由于 Unity 摄像机纹理和编码输入的纵向约定不同，该后端在 FFmpeg 过滤链中使用一次垂直翻转。

### 游戏画面后端

游戏画面模式在 `WaitForEndOfFrame` 后调用 Unity 6 的 `ScreenCapture.CaptureScreenshotIntoRenderTexture`。输出尺寸在会话开始时固定为 `Screen.width × Screen.height`，不做二次缩放；窗口尺寸改变会终止本次任务，避免读取已经失效的纹理。

Unity 的最终画面捕获已经符合当前编码输入方向，因此该后端不再应用摄像机模式的垂直翻转。它可以包含额外摄像机、Screen Space UI、Overlay Canvas 和最终屏幕效果。

### 帧管线与内存

两个后端都实现 `IChartFrameCapture`，返回统一的 `ChartPendingFrame`：

- 使用 RGBA 或实验性 BGRA 回读。
- 用递增帧序号保持异步回读后的输出顺序。
- 对谱面“限制帧率”事件复用上一画面，但仍写出完整输出帧率。
- 使用数组池和有界写入队列复用大块帧缓冲。
- 按输出分辨率动态限制 pending readback 和编码缓存，避免 4K/8K 峰值失控。
- 成功、取消和异常路径都必须等待或释放未完成资源。

### 音频与视频封装

`ChartUnityAudioCapture` 使用 `AudioRenderer` 捕获游戏最终混音，写入临时浮点 WAV。界面提示音通过渲染补丁排除，谱面音乐、打击音、长按音和 `PlaySound` 事件仍会进入成品。

`FfmpegEncoder` 以标准输入接收原始视频帧，优先使用 NVIDIA H.264 硬件编码，并按档位回退到 CPU H.264。最终容器支持 MP4、MKV、MOV，音频支持 AAC、FLAC 和 ALAC。偶数尺寸和 `yuv420p` 由合成阶段统一保证。

FFmpeg 作为独立进程调用，不链接进 Mod DLL。当前固定版本为 8.1.2 gyan.dev essentials build；版本、构建信息、许可证和对应源码说明随发布包分发。

## 压缩包处理栈

`ArchiveIoPatches` 接管游戏的 `ZipUtils.Unzip` 和 `ZipUtils.Zip`。初始化阶段从 Mod 目录加载：

```text
SharpSevenZip.dll
ThirdParty/7-Zip/x64/7z.dll
```

初始化会检查 `7z.dll` 是否存在、PE 头是否有效以及是否为 AMD64。失败时 Archive I/O 整组回滚，游戏原有方法保持未修改状态。

### 解压

- 按文件签名识别 ZIP、RAR、7z、TAR、GZip、BZip2、XZ、CAB 等格式，不只依赖扩展名。
- 对 TGZ、TBZ2、TXZ 等压缩 TAR 完成外层解压和内层 TAR 展开。
- ZIP 文件名优先使用 UTF-8 标志和有效的 Unicode Path Extra Field。
- 旧 ZIP 可手动指定 CP949、GB18030、Shift-JIS 或 CP437。
- 自动模式对整个压缩包统一选码，执行严格解码、字节往返、路径合法性、谱面资源引用和文字分布评分。
- 限制最多 10,000 个条目和 2,000 MiB 解压总量。
- 拒绝绝对路径、目录穿越、重复目标、大小写不敏感冲突和覆盖已有文件。

### 压缩

编辑器导出仍创建标准 ZIP 格式的 `.adozip`：

- `main.adofai` 与 `subN.adofai` 位于包根目录。
- 资源保留相对于谱面目录的子路径。
- 条目分隔符统一为 `/`。
- 文件名使用 Unicode。
- 创建前检查大小写不敏感重复路径。
- 先生成临时文件，成功后再移动到最终位置，避免留下半成品。

SharpSevenZip 固定为 2.0.109。托管程序集放在 Mod 根目录，原生库和许可证放在 `ThirdParty`；用户无需单独安装 7-Zip。

## 设置、本地化和云同步

`Settings` 继承 `UnityModManager.ModSettings`。Web 页面修改字段，保存前由 `Normalize()` 规范化范围和枚举值。渲染会话开始时复制本次任务所需的设置，渲染途中修改只影响下一次任务。

本地化数据位于 `Resources/localization.json`。`Localization` 根据 Unity `SystemLanguage` 选择中文或英文；缺少当前语言时回退英文，键不存在时返回键名，避免文本缺失阻止 Mod 加载。

Steam 云同步是显式的上传/下载操作。`CloudSettingsManager` 把受支持字段转换为普通键值数据并使用游戏已有的 Steam 环境读写，不在启动或退出时自动覆盖本地设置。

## 构建和发行链

`ADOFAI.EditorTweaks.csproj` 是 SDK 风格项目，使用 .NET SDK 驱动 MSBuild，但目标程序集为 `net481`。`GameExePath` 用于定位当前游戏和 `Managed` 目录。

`ADOFAIMod.targets` 的主要阶段：

1. 验证游戏路径并把所需官方程序集同步到 `lib/`。
2. 确保 FFmpeg 可执行文件和再分发说明存在。
3. 可选更新 `Info.json` 版本。
4. 编译 Mod DLL。
5. 清空并重建 `out/`，复制托管依赖、Resources、ThirdParty 和许可证。
6. 生成 `Build/<ModId>-<Version>/` 与同名 ZIP。
7. 部署到游戏 `Mods` 目录；只有显式开启时才启动游戏。

日常验证：

```powershell
dotnet build
```

GitHub Actions 在 `1.2.3` 或 `v1.2.3` 形式的标签推送后运行 Windows 构建，先校验标签版本与 `Info.json` 一致，再上传打包产物并创建 GitHub Release。工作流使用 .NET SDK 8.0 驱动构建，但不会改变项目的 `net481` 目标。

## 第三方组件与许可证

| 组件 | 分发方式 | 许可证资料 |
| --- | --- | --- |
| Harmony | 由游戏的 UnityModManager 环境提供 | 跟随对应运行环境。 |
| SharpSevenZip 2.0.109 | 独立托管 DLL | `ThirdParty/SharpSevenZip/LICENSE.txt`。 |
| 7-Zip x64 `7z.dll` | 独立原生 DLL | `ThirdParty/7-Zip/License.txt`。 |
| FFmpeg 8.1.2 | 独立可执行文件 | `ThirdParty/FFmpeg/GPL-3.0.txt`、`ThirdParty/FFmpeg/FFmpeg-*.txt`。 |

发布前必须确认这些文件都进入最终包，且 FFmpeg 对应源码资料与实际二进制版本一致。
