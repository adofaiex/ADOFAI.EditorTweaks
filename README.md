# ADOFAI Editor Tweaks

ADOFAI Editor Tweaks 是一个用于 **A Dance of Fire and Ice** 的 UnityModManager Mod。它的定位不是做大型功能包，而是把编辑器里长期影响工作流的细节补齐，并提供一个可以直接导出谱面视频的离线渲染器。

版本变化见 [CHANGELOG.md](CHANGELOG.md)。本文档描述当前代码的功能，不重复维护具体版本号。

当前主要包含：

- 编辑器数值输入框右键拖动调节。
- Camera / CameraAspect 装饰拖动、轴心显示和移动吸附修复。
- 从中途播放时的视频背景同步修复。
- 官方编辑器偏好设置即时保存。
- 常见谱面压缩包读取、旧 ZIP 多语言文件名识别和兼容原版的 ADOZIP 导出。
- 独立的本地 Web 设置页，支持实时状态和渲染进度。
- 自定义谱面、官谱、`scnGame` 场景下的离线定帧视频渲染。
- 摄像机纯净画面与游戏最终画面两种渲染来源。
- 面向其他 Mod 的强类型谱面渲染任务 API。
- 渲染时的画面、音频、输入、UI 遮罩和诊断日志。

玩家操作请看 [用户手册](Resources/README.html)。开发资料放在 [Doc](Doc/README.md)，完整技术选型见 [技术栈与运行时依赖](Doc/TechnologyStack.md)。

## 项目结构

```text
.
├── ADOFAI.EditorTweaks.csproj
├── ADOFAIMod.targets
├── build-dev.bat
├── build-release.bat
├── CHANGELOG.md
├── Info.json
├── README.md
├── Doc/
│   ├── Architecture.md
│   ├── TechnologyStack.md
│   ├── BuildAndRelease.md
│   ├── PatchInventory.md
│   ├── ChartRendering.md
│   ├── DecorationSelection.md
│   ├── WebUi.md
│   ├── EditorPreferences.md
│   ├── NumericDrag.md
│   ├── SettingsAndLocalization.md
│   └── VideoBackgroundSync.md
├── Resources/
│   ├── README.html
│   └── localization.json
├── scripts/
│   ├── BumpModVersion.ps1
│   ├── EnsureFfmpeg.ps1
│   └── PackageMod.ps1
└── src/
    ├── Main.cs
    ├── Patching/
    ├── Settings.cs
    ├── Localization.cs
    └── Features/
        ├── ArchiveIo/
        ├── ChartRendering/
        ├── CloudSettings/
        ├── DecorationSelection/
        ├── RenderInputGuard/
        ├── WebUi/
        ├── EditorPreferences/
        ├── NumericDrag/
        └── VideoBackgroundSync/
```

## 运行入口

UnityModManager 加载 `ADOFAI.EditorTweaks.Main.Load`。

`Main.Load` 做三件事：

1. 加载 `Resources/localization.json`。
2. 加载并补全 `Settings` 默认值。
3. 注册 UMM 回调：`OnToggle`、`OnGUI`、`OnSaveGUI`。

启用 Mod 时：

- `PatchManager.ApplyAll(modEntry.Info.Id)` 按功能组独立应用 Patch。
- 任一组失败时只回滚该组，其他功能继续启用。
- `WebUiHost.Ensure()` 启动只监听 `127.0.0.1` 的本地 HTTP 服务，不会自动打开浏览器。

禁用 Mod 时：

- `WebUiHost.Destroy()` 停止 HTTP/SSE 服务、取消渲染任务并清理连接。
- `PatchManager.UnpatchAll()` 清理所有分组 Harmony ID。

## 功能总览

### 数值输入框右键拖动

官方编辑器里很多数字输入框只能点进去手输。Mod 会在 `PropertyControl_Text.Setup` 和 `PropertyControl_Vector2.Setup` 后，把游戏已有的 `DraggableNumberInputField` 组件挂到支持的 TMP 输入框上。

支持：

- `Int`
- `Float`
- `Tile`
- `Vector2`

交互方式是右键按住横向拖动。拖动时实时写回当前选中的 `LevelEvent`，并刷新装饰、背景、滑条、地板事件指示器等编辑器状态。松开后调用官方输入框的 `onEndEdit`，让官方保存逻辑继续执行。

关键 Patch：

- `PropertyControl_Text.Setup`：Postfix 附加拖动组件。
- `PropertyControl_Vector2.Setup`：Postfix 分别给 X/Y 输入框附加拖动组件。
- `DraggableNumberInputField.OnPointerDown`：Prefix 接管本 Mod 创建的拖动字段，只允许右键开始拖动。
- `DraggableNumberInputField.OnPointerUp`：Prefix 在拖动结束时提交值。
- `DraggableNumberInputField.SetArrowsVisible`：Prefix 避免官方组件在没有箭头对象时访问空数组。

详见 [Doc/NumericDrag.md](Doc/NumericDrag.md)。

### 装饰选择与拖动修复

这一组修复集中在 `src/Features/DecorationSelection`。

Camera / CameraAspect 相对装饰的原版拖动容易把屏幕空间和世界空间混在一起。Mod 在拖动开始时记录装饰数据坐标，拖动过程中按相机正交尺寸和宽高比计算屏幕空间增量，再写回 `LevelEvent["position"]`。

此外还修复：

- Shift 轴锁定在修复路径中继续有效。
- Camera / CameraAspect 装饰的轴心十字跟随实际屏幕位置。
- 拖动后按配置的步进吸附坐标。

关键 Patch：

- `scnEditor.DragDecorationsStart`：Postfix 修正拖动起点缓存。
- `scnEditor.DragDecorations`：Prefix 接管包含 Camera / CameraAspect 装饰的拖动。
- `scnEditor.DragDecorations`：Postfix 对最终坐标做吸附。
- `DecorationPivot.UpdatePivotCrossImage`：Prefix 直接把轴心十字放到装饰 Transform 位置。
- `scrDecoration.UpdateScreenClamp`：Postfix 修正屏幕相对装饰的 `scrParallax` 屏幕坐标。
- `scrParallax.SetTrans`：Postfix 在视差变换后刷新轴心十字。

详见 [Doc/DecorationSelection.md](Doc/DecorationSelection.md)。

### 视频背景同步修复

官方视频背景启动逻辑会在 `VideoPlayer.Prepare()` 结束后设置一次 `VideoPlayer.time`。从 checkpoint 或编辑器选中地板开始播放时，长视频随机 seek 可能慢半拍，最终表现为视频背景落后或漂移。

Mod 在 `scrVfxPlus.Update` 后持续检查一段启动窗口，目标时间使用官方时间模型：

```text
songposition_minusi - countdownOffset + vidOffset
```

循环视频会按视频长度取模，非循环视频会 clamp 到视频末尾之前。

关键 Patch：

- `scrVfxPlus.Reset`：Postfix 清理每个 VFX 实例的同步状态。
- `scrVfxPlus.Update`：Postfix 根据谱面时间校正 `VideoPlayer.time` 和 `playbackSpeed`。

详见 [Doc/VideoBackgroundSync.md](Doc/VideoBackgroundSync.md)。

### 编辑器偏好设置即时保存

官方编辑器偏好有时只改了内存状态，没有立刻写入持久化配置。Mod 在偏好项通知变化后调用 `Persistence.generalPrefs.Save()`。

关键 Patch：

- `EditorPreferencesEntry.NotifyChange`：Postfix 保存官方 general preferences。

详见 [Doc/EditorPreferences.md](Doc/EditorPreferences.md)。

### Web 设置页与快捷键

Mod 启用后会在本机启动 HTTP 服务，但不会自动弹出浏览器。UMM 设置面板只保留“打开 Web 设置页面快捷键”一项，默认是 `Ctrl+Shift+E`，点击“录入快捷键”后按下新的组合键即可保存。

Web 页面内包含全部设置、兼容性状态、云同步入口和谱面渲染进度。页面通过 HTTP 修改设置，通过 SSE 接收设置变化、渲染进度和最终结果。服务只绑定 `127.0.0.1`，每次启动生成随机访问令牌。

渲染期间页面会切换为完整进度视图；编辑器输入、玩家输入和 Unity UI 输入会被输入保护 Patch 屏蔽，但 `scrController.Update` 仍继续运行，以保证渲染时钟正常推进。按 `Esc` 或点击页面中的取消按钮都调用同一个渲染任务取消入口。详见 [Doc/WebUi.md](Doc/WebUi.md)。

### 压缩包处理

`ArchiveIo` 功能组接管游戏的 `ZipUtils.Unzip` 和 `ZipUtils.Zip`。读取侧使用 SharpSevenZip 2.0.109 和随 Mod 分发的 x64 `7z.dll`，支持 ZIP、ADOZIP、RAR、7z、TAR、GZip、BZip2、XZ、CAB 及常见压缩 TAR。

ZIP 文件名解析由 Mod 负责：优先采用 UTF-8 标志或有效 Unicode Path Extra Field；旧包可以自动识别或手动指定 CP949、GB18030、Shift-JIS、CP437。自动模式以整个压缩包为单位，结合严格字节往返、路径合法性、谱面资源引用和文字分布评分。

解压继续限制 10,000 条目和 2,000 MiB 总量，并拒绝路径穿越、绝对路径、重复目标和覆盖已有文件。编辑器导出仍创建标准 ZIP 格式的 `.adozip`，保留资源相对目录和 Unicode 文件名，确保未安装 Mod 的原版游戏仍能读取。

补丁初始化会验证托管依赖与 x64 `7z.dll`。失败时只回滚 Archive I/O 组并保留游戏原有压缩方法。实现与依赖说明见 [Doc/TechnologyStack.md](Doc/TechnologyStack.md)，补丁入口见 [Doc/PatchInventory.md](Doc/PatchInventory.md)。

## 离线谱面视频渲染

这是当前 Mod 最大的功能。它直接从 Unity 游戏画面导出视频，不录制 Windows 桌面。默认摄像机模式只导出干净的谱面画面；兼容模式也可以导出包含游戏和编辑器 UI 的最终游戏画面。

其他 Mod 可以引用 `ADOFAI.EditorTweaks.dll`，通过 `ADOFAI.EditorTweaks.Api.Rendering.ChartRenderApi` 创建独立请求、启动或附着渲染、读取进度、正常完成和取消任务。Web 设置页也使用同一入口。公共接口不暴露 Unity 捕获器、Harmony、Settings 或 FFmpeg 实例，详见 [Doc/Api/ChartRendering.md](Doc/Api/ChartRendering.md)。

### 支持场景

渲染入口在 Web 设置页的“谱面渲染”模块中。可渲染条件由 `ChartRenderSession.IsPlayableLevelLoaded()` 判断：

- 编辑器中加载的自定义谱面。
- `scnGame` 自定义关卡场景。
- 官谱和旧官谱直场景，只要 `ADOBase.controller.gameworld` 存在且 `ADOBase.lm.listFloors` 可用。

编辑器渲染使用 `scnEditor.Play()` 走官方编辑器播放路径；游戏场景渲染使用控制器、conductor 和 level maker 当前状态启动或接管播放。

### 渲染流程

`ChartRenderSession.Run()` 是主协程，核心流程如下：

1. `TryPrepare()` 创建工作目录、导出目录、临时视频路径、临时音频路径和 `render.log`。
2. `TryStartPlayback()` 保存旧状态，设置 `Time.captureFramerate`、关闭 vSync、提高 `Application.targetFrameRate`。
3. 编辑器环境由 `ChartRenderPlaybackController` 调用官方播放路径：整首渲染选择第 0 块；片段渲染选择当前框选段落的第一个砖块；随后设置 checkpoint，`RDC.auto = false`，调用 `editor.Play()`。
4. 游戏环境调用 `StartGameScenePlayback()`：必要时停止等待开始协程、隐藏 Press To Start、rewind conductor、`Start_Rewind()`，并对自定义关卡调用 `FinishCustomLevelLoading()`。
5. 等待 `ADOBase.conductor` 确认播放已经 schedule。
6. `BeginForcedVisualClock()` 锚定视觉时钟。
7. 创建 `ChartRenderMemoryBudget` 和 `ChartRenderFramePipeline`，按分辨率限制 GPU 回读和 FFmpeg 写入队列。
8. 根据设置创建摄像机或游戏画面捕获后端，并输出到专用 `RenderTexture`。
9. 创建 `ChartUnityAudioCapture`，使用 Unity `AudioRenderer` 离线捕获音频。
10. 创建 `FfmpegEncoder`，按编码档位选择 NVENC 或 x264。
11. 锚定视觉时钟后才启用 `RDC.auto` 和 `IsAutoPlaybackReady`。
12. 每一帧等待 `WaitForEndOfFrame()`，捕获音频，提交 GPU readback，推进强制视觉时间。
13. 检测到谱面结束后继续录制尾巴秒数。
14. 完成视频编码，再把 WAV 音频 mux 成最终 MP4。
15. 恢复编辑器、`RDC.auto`、checkpoint、`Time.captureFramerate`、`Application.targetFrameRate` 和 vSync。

### 画面捕获方式

- **摄像机渲染（默认、推荐）**：沿用官方 `Bgcamstatic`、`BGcam`、`camobj` 摄像机链。它不包含屏幕空间 UI，能够独立于游戏窗口按目标分辨率渲染，适合普通谱面和高分辨率导出。
- **游戏画面渲染（兼容模式）**：在 `WaitForEndOfFrame` 后捕获 Unity 最终游戏画面，包含额外摄像机、编辑器 UI、游戏 UI 和屏幕空间 Canvas，适合摄像机模式遗漏效果的特殊谱面。

游戏画面模式直接使用开始渲染时的 `Screen.width × Screen.height` 作为成品分辨率，不读取摄像机模式的输出宽高设置。渲染期间必须保持窗口尺寸不变，否则任务会安全失败并恢复播放状态。

这是因为该模式读取的是已经按当前 backbuffer 分辨率完成合成的最终帧；把 1080p 最终帧写入 4K 纹理只能得到插值放大，不能增加几何、UI 或后处理的真实采样。需要原生高分辨率时使用摄像机模式，或先把游戏实际分辨率设到目标大小。两条管线的完整技术栈和分辨率限制见 [Doc/ChartRendering.md](Doc/ChartRendering.md#画面捕获)。

为避免把 Mod UI 录入成品，渲染进度只显示在外部 Web 设置页；按 `Esc` 可以取消。渲染预览设置只影响摄像机模式。

### 选中段落渲染

默认仍然渲染整首谱面。开启“仅渲染选中段落”后，需要在编辑器中框选至少两个连续砖块。渲染器会从选区第一个砖块开始，到选区最后一个砖块后停止，不使用整首渲染的尾巴秒数。

片段启动时会先等官方 checkpoint 准备完成，进入实际游玩状态后才开始采集画面和音频，避免把 checkpoint 静音淡入和黑场准备阶段录进成品。

片段渲染不会改变整首渲染的第 0 格启动路径。输出文件名会追加范围后缀，例如 `SongName_f32-f64_yyyyMMdd_HHmmss.mp4`。

### 为什么要做强制视觉时钟

ADOFAI 的视觉逻辑大量依赖 `scrConductor.songposition_minusi`。离线渲染时，Unity 的真实执行速度和输出帧率不是同一个东西：机器慢一点只是等待时间变长，成品仍应该是严格 60fps 或用户设置的 fps。

如果只设置 `Time.captureFramerate`，仍可能遇到两个问题：

- 新版官方 `scrConductor` / 输入校准逻辑会把输入偏移、实际音频时间、异步输入角度修正揉进视觉时间。
- 自动打击如果某帧落后，会出现连续追块、球突然跳过多个砖块，视频里看起来就是球抽搐或乱飘。

当前修复由三块组成：

1. `ChartRenderVisualClock` 在播放真正开始后记录当时的 `songposition_minusi`，然后每个输出帧把视觉时间设为 `startSongPosition + frameIndex / fps * pitch`。
2. Patch `scrConductor.set_songposition_minusi`，渲染期间无论游戏内部要写什么值，都替换为强制帧时间。
3. Patch `scrConductor.get_calibration_i`，渲染期间返回 `0`，避免玩家输入偏移影响视觉相位。音频本身由 Unity 音频渲染输出，不需要把玩家输入偏移叠到画面上。

这就是之前“球抽搐”和“视觉时间轴起点与音频起点有很小相位差”的核心修复点。不要再按旧版兼容方式去改 `scrConductor` 旧逻辑；当前代码按新版游戏的 `scrPlayerManager` / `scrPlayer` / `scrConductor` 写。

### 自动打击与球抽搐防线

`ChartRenderAutoPlayer` 在 `scrConductor.Update` 的 Postfix 执行。它读取当前玩家、当前地板、下一块地板的 `entryTime`，只要强制视觉时间已经到达下一块，就调用 `scrPlayer.Hit(isAuto: true)` 补打。

为了避免异常追块：

- 每帧最多自动命中 16 次，超过会写入 `AUTO_HIT_GUARD_REACHED`。
- 片段渲染时，自动命中不会超过选中段落终点。
- 打击前刷新当前 chosen planet 角度。
- 打击前把非 midspin 的球角度对齐到 `targetExitAngle`。
- 清理 multipress 相关状态，避免自动播放被多押惩罚影响。
- Patch `AsyncInputUtils.AdjustAngle(scrPlayer, ulong)`，渲染时直接跳过异步输入角度修正，并计入诊断日志。

相关 Patch：

- `scrConductor.Update`：Postfix 自动补打。
- `AsyncInputUtils.AdjustAngle(scrPlayer, ulong)`：Prefix 渲染时 suppress。
- `scrConductor.set_songposition_minusi`：Prefix 强制视觉时间。
- `scrConductor.get_calibration_i`：Prefix 渲染时去掉输入偏移。

### 画面捕获

`ChartFrameCapture` 使用官方 `scrCamera` 的相机链，而不是自己新建一个相机：

- `scrCamera.Bgcamstatic`
- `scrCamera.BGcam`
- `scrCamera.camobj`

三台相机的 `targetTexture` 被临时指向同一个 `RenderTexture`。同时打开 `Overlaycam` 和 `quad`，并把 `quad` 的材质主纹理替换为捕获目标，保持官方相机合成链一致。这样官谱、旧官谱场景、滤镜、背景和视频背景都更接近游戏内实际画面。

捕获默认使用 `AsyncGPUReadback.Request(captureTarget, 0, TextureFormat.RGBA32)`。高级设置可切到实验性的 `BGRA32` 路径；如果运行时不支持 BGRA，会回退到 RGBA。

高分辨率渲染会按单帧大小限制队列：

- 1080p 单帧约 7.9 MiB，GPU 回读最多 8 帧。
- 1440p 单帧约 14.1 MiB，GPU 回读最多 6 帧。
- 4K 单帧约 31.6 MiB，GPU 回读最多 4 帧。
- 8K 单帧约 126.6 MiB，GPU 回读最多 2 帧。

FFmpeg 写入队列也按内存预算计算，不再固定缓存大量帧。队列满时会反压渲染推进，宁可慢一点，也避免内存峰值失控。

### 音频捕获与合成

旧设计曾考虑手工混合原曲和各种音效，但更稳定的方式是直接使用 Unity 的离线音频渲染：

- `AudioRenderer.Start()`
- 每帧 `AudioRenderer.GetSampleCountForCaptureFrame()`
- `AudioRenderer.Render(samples)`
- 写入 float32 WAV
- 视频完成后由 FFmpeg mux 成 AAC

这样原曲、打击音、长按音效、`PlaySound`、视频背景相关音频等只要走 Unity mixer，就会以游戏实际播放结果进入 WAV。为了避免 UMM 和菜单点击声进入成品，Patch 了 `scrSfx.PlaySfx(AudioClip, MixerGroup.InterfaceParent, ...)`，渲染期间对 InterfaceParent 组直接返回原 clip，不实际播放。

如果 Unity 连续没有返回音频样本，渲染器会按当前帧率计算容错时间，并在连续 1 秒不可用时重启一次音频捕获。恢复后仍连续 5 秒没有样本才会停止任务，因此 120 FPS 不再使用固定 30 帧、仅 0.25 秒的过短容错。实际音频仍只保存 Unity 原样返回的连续数据，不会按视频帧插入静音或截断采样。

关键 Patch：

- `scrSfx.PlaySfx(AudioClip, MixerGroup, float, float, float)`：Prefix 渲染时屏蔽 InterfaceParent。

### FFmpeg 编码

`FfmpegEncoder` 使用两阶段输出：

1. raw RGBA 帧进入临时无音频 MP4；实验 BGRA 模式会改用 raw BGRA。
2. WAV 音频和临时 MP4 mux 成最终 MP4。

视频参数：

- 输入：`-f rawvideo -pixel_format rgba -video_size WxH -framerate FPS -i -`
- 翻转：摄像机模式使用 `vflip`；游戏画面模式的回读方向已经正确，不再重复翻转。
- 像素格式：`yuv420p`，保证常见播放器和网站兼容。

编码档位：

- 自动均衡：默认，优先 NVENC `p4`，失败时回退 x264 `veryfast`。
- 最快：NVENC `p1` 或 x264 `ultrafast`。
- 均衡：NVENC `p4` 或 x264 `veryfast`。
- 质量：NVENC `p6` 或 x264 `fast`。
- CPU 兼容：强制 x264。
- 自定义：保留 `cpu`、`x264`、`x264:<preset>` 等高级手填方式。

码率限制：

- 自动推荐：设置为 `0` 时按分辨率和帧率自动计算。
- 1080p60 推荐约 `20 Mbps`。
- 2K60 推荐约 `35 Mbps`。
- 4K60 推荐约 `60 Mbps`。
- 编码时同时设置目标码率、最大码率和缓冲区，避免 4K 文件过大或播放器遇到过高峰值码率卡顿。

### 诊断日志

每次渲染会在工作区 `CurrentRender/render.log` 写日志。它记录：

- 渲染开始时间。
- 相机链名称和 depth。
- 当前场景、关卡名、是否 `scnGame`。
- 视觉时钟锚点、pitch、addoffset、被抑制的 input offset。
- 自动打击次数、失败次数、跳块次数。
- 异步角度修正被 suppress 的次数。
- FFmpeg mux 参数和失败输出。

排查球抽搐时重点看：

- `SONG_MOVED_BACKWARD`
- `PLAYER_FAILED`
- `AUTO_HIT ... FLOOR_JUMP`
- `AUTO_HIT_GUARD_REACHED`
- `suppressedAsyncAdjusts`

如果 `autoHits` 正常、`failedAutoHits=0`、`floorJumps=0`，基本可以认为自动播放和视觉时钟没有异常。

## Patch 总表

完整说明见 [Doc/PatchInventory.md](Doc/PatchInventory.md)。这里列出全部 Harmony Patch：

| 模块 | 目标方法 | 类型 | 作用 |
| --- | --- | --- | --- |
| NumericDrag | `PropertyControl_Text.Setup` | Postfix | 给数字输入框附加拖动组件 |
| NumericDrag | `PropertyControl_Vector2.Setup` | Postfix | 给 Vector2 的 X/Y 输入框附加拖动组件 |
| NumericDrag | `DraggableNumberInputField.OnPointerDown` | Prefix | 接管右键拖动开始 |
| NumericDrag | `DraggableNumberInputField.OnPointerUp` | Prefix | 拖动结束后提交 |
| NumericDrag | `DraggableNumberInputField.SetArrowsVisible` | Prefix | 防空箭头数组 |
| DecorationSelection | `scnEditor.DragDecorationsStart` | Postfix | 修正 Camera 相对装饰拖动起点 |
| DecorationSelection | `scnEditor.DragDecorations` | Prefix | 接管 Camera / CameraAspect 装饰拖动 |
| DecorationSelection | `scnEditor.DragDecorations` | Postfix | 吸附装饰坐标 |
| DecorationSelection | `DecorationPivot.UpdatePivotCrossImage` | Prefix | 修正装饰轴心十字 |
| DecorationSelection | `scrDecoration.UpdateScreenClamp` | Postfix | 修正屏幕相对坐标 |
| DecorationSelection | `scrParallax.SetTrans` | Postfix | 视差更新后刷新轴心 |
| VideoBackgroundSync | `scrVfxPlus.Reset` | Postfix | 清理视频同步状态 |
| VideoBackgroundSync | `scrVfxPlus.Update` | Postfix | 追踪并校正视频背景时间 |
| EditorPreferences | `EditorPreferencesEntry.NotifyChange` | Postfix | 立即保存官方偏好 |
| RenderInputGuard | `scnEditor.Update` | Prefix | 渲染时拦截编辑器输入 |
| RenderInputGuard | `scnEditor.ZoomCamera` | Prefix | 渲染时阻止滚轮缩放 |
| RenderInputGuard | `scrController.Update` | Prefix | 保持控制器更新，不阻止渲染时钟 |
| RenderInputGuard | `scrController.TogglePauseGame` | Prefix | 渲染时阻止暂停 |
| RenderInputGuard | `scrPlayerManager.AnyValidInputWasTriggered` | Prefix | 渲染时屏蔽玩家输入 |
| RenderInputGuard | `scrPlayer.ValidInputWasTriggered` | Prefix | 渲染时屏蔽按下 |
| RenderInputGuard | `scrPlayer.ValidInputWasReleased` | Prefix | 渲染时屏蔽松开 |
| RenderInputGuard | `scrPlayer.CountValidKeysPressed` | Prefix | 渲染时按键数为 0 |
| RenderInputGuard | `StandaloneInputModule.Process` | Prefix | 渲染时阻止 Unity UI 背景点击 |
| ChartRendering | `scrConductor.set_songposition_minusi` | Prefix | 强制离线视觉时钟 |
| ChartRendering | `scrConductor.get_songposition_minusi` | Postfix | 读取时返回离线视觉时间 |
| ChartRendering | `scrConductor.Update` | Postfix | 自动补打到当前帧 |
| ChartRendering | `scrPlayer.Hit` | Prefix | 片段渲染时阻止命中选区终点之后的砖块 |
| ChartRendering | `AsyncInputUtils.AdjustAngle(scrPlayer, ulong)` | Prefix | 防止异步输入角度修正造成跳动 |
| ChartRendering | `scrSfx.PlaySfx(AudioClip, MixerGroup, float, float, float)` | Prefix | 屏蔽界面音进入渲染音频 |
| ChartRendering | `scrHitTextManager.ShowHitText` | Prefix | 控制导出时是否显示判定文字 |
| ChartRendering | `scrCamera.UpdateCustomFrameRateScreen` | Prefix | 保留谱面限制帧率效果 |
| ArchiveIo | `ZipUtils.Unzip` | Prefix | 接管常见压缩包解压和旧 ZIP 文件名识别 |
| ArchiveIo | `ZipUtils.Zip` | Prefix | 导出保留资源目录的标准 ZIP/ADOZIP |
| LevelLoading | `scnEditor.UpdateImageLoadResult` | Prefix | 合并重复缺图记录，防止谱面加载中断 |

## 设置

UMM 设置面板只负责录入打开 Web 设置页的快捷键。其余设置都在 Web 页面中修改并自动保存；渲染相关设置会在下一次渲染开始时读取。

基础渲染设置：

- 导出目录。
- 画面捕获方式：摄像机渲染或游戏画面渲染。
- 分辨率快捷预设：1080p、2K、4K。
- 视频宽度。
- 视频高度。
- 帧率快捷预设：30、60、120。
- 帧率。
- 结束后延迟停止秒数。
- 是否显示判定文字。
- 是否仅渲染编辑器当前选中段落。
- 在工具页一键恢复全部默认设置。

分辨率预设和宽高只用于摄像机模式。游戏画面模式直接使用任务开始时的游戏分辨率。

Web 页面中的高级渲染设置默认隐藏：

- 工作区目录。
- 画质参数 CRF / QP。
- 视频码率。默认自动推荐：1080p60 约 20 Mbps、2K60 约 35 Mbps、4K60 约 60 Mbps。
- 编码档位。
- 实验性回读格式。
- 音频格式和视频容器。
- 渲染预览模式。
- 音频同步偏移，正数让音频提前，负数让音频延后。

默认推荐是 `1920x1080 @ 60fps`，优先 GPU 硬编码。渲染进行时相关设置会锁定，任务完成、失败或取消后恢复编辑；工具页提供一键恢复全部默认设置。

## 构建与部署

项目目标框架是 `net481`，通过游戏 managed assemblies 编译。当前项目文件里的默认游戏路径是：

```text
C:\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice.exe
```

开发构建：

```bat
build-dev.bat
```

正式发行：

```bat
build-release.bat
build-release.bat Patch
build-release.bat Minor
build-release.bat Major
```

直接构建：

```powershell
dotnet build
```

`ADOFAIMod.targets` 会：

- 验证 `GameExePath`。
- 如果 `ThirdParty/FFmpeg/ffmpeg.exe` 不存在，则运行 `scripts/EnsureFfmpeg.ps1` 下载 FFmpeg。
- 调用 `webui/npm run build` 构建 React 页面，并只把 `webui/dist` 复制到 `out/Resources/WebUI`。
- 清空并重建 `out/`。
- 复制 DLL、托管依赖、`Info.json`、`Resources`、`ThirdParty` 和许可证到 `out/`。
- 生成 `Build/<ModId>-<Version>/`。
- 生成 `Build/<ModId>-<Version>.zip`。
- 部署到游戏目录 `Mods/ADOFAI.EditorTweaks/`。
- `AutoLaunchGame=false` 时不自动启动游戏。

正式发行脚本会自动递增 `Info.json` 版本号。普通开发构建不会改版本号。

`ThirdParty/FFmpeg/ffmpeg.exe` 不应提交到 git。

详见 [Doc/BuildAndRelease.md](Doc/BuildAndRelease.md)。版本变化见 [CHANGELOG.md](CHANGELOG.md)。

## 开发注意事项

- 新功能优先放在 `src/Features/<FeatureName>/`。
- Harmony Patch 尽量小而明确，Patch 表必须同步更新。
- 用户可见文本放进 `Resources/localization.json`。
- 新增设置要同步更新 `Settings.cs`、Web UI、HTTP 状态接口和文档。
- 新增用户可见功能要同步更新 `CHANGELOG.md`。
- 修改构建或发行流程要同步更新 `Doc/BuildAndRelease.md`。
- 修改渲染时必须同时考虑：视觉时钟、自动打击、音频捕获、FFmpeg、输入遮罩、取消恢复和诊断日志。
- 不要为旧版 ADOFAI 保留复杂兼容分支。当前 Mod 按新版官方源码和新版 `scrPlayerManager` / `scrPlayer` / `scrConductor` 行为实现。
- 如果再次出现球抽搐，先看 `render.log`，不要先回滚视觉时钟或自动打击逻辑。

## 验证清单

至少运行：

```powershell
dotnet build
```

建议在游戏中检查：

- UMM 面板中的快捷键录入、本地化和保存正常。
- `Ctrl+Shift+E` 能打开本地 Web 设置页，页面刷新后设置保持。
- Web 渲染进度页出现时，背景不能点击、滚轮不能缩放、键盘不能暂停或触发游戏输入。
- 点击取消后，编辑器能自动回到编辑模式。
- 数值输入框可右键拖动，拖动时实时刷新。
- Camera / CameraAspect 装饰拖动符合屏幕空间直觉。
- 装饰吸附值为 `0` 时关闭吸附。
- 从中途播放带视频背景的谱面，视频背景不明显延迟。
- 自定义谱面、官谱、`scnGame` 场景都能开始渲染。
- 摄像机模式成品不包含编辑器 UI、UMM UI、进度窗或菜单音效。
- 游戏画面模式包含最终游戏和编辑器 UI；输出跟随游戏分辨率且方向正确。
- 成品分辨率、帧率、尾巴秒数符合设置。
- 音频和画面对齐，结尾不被切掉。
- `render.log` 里没有 `PLAYER_FAILED`、异常 `FLOOR_JUMP` 或 FFmpeg 错误。
