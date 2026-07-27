# ChartRendering 模块

`src/Features/ChartRendering` 是谱面视频渲染器。它负责把当前游戏内关卡离线导出为 MP4，核心目标是：

- 不录桌面。
- 不录编辑器 UI 或 UMM UI。
- 支持编辑器自定义谱面、`scnGame` 自定义关卡、官谱和旧官谱场景。
- 成品帧率由设置决定，机器慢只影响等待时间，不影响视频时间轴。
- 音频直接来自 Unity mixer 离线渲染，尽量贴近游戏实际播放结果。

## 文件职责

| 文件 | 职责 |
| --- | --- |
| `ChartRenderSession.cs` | 一次渲染的主协程。负责状态机、阶段切换、结束检测、取消和总调度。 |
| `ChartRenderPlaybackController.cs` | 保存/恢复编辑器状态，按整首或片段起点启动官方播放路径，管理 `RDC.auto` 启用时机。 |
| `ChartRenderFramePipeline.cs` | 管理 GPU readback pending 队列、帧 buffer pool、FFmpeg 写入反压。 |
| `ChartRenderMemoryBudget.cs` | 按输出分辨率计算单帧大小、内存预算、GPU pending 上限和 FFmpeg 队列上限。 |
| `ChartRenderProgressModel.cs` | 计算进度、处理速度、ETA、重复帧比例和流畅度提示。 |
| `ChartRenderOptionValues.cs` | 统一管理编码档位、回读格式、预览模式等设置值。 |
| `ChartRenderRange.cs` | 解析整首/选中段落渲染范围，估算片段时长，提供片段结束检测。 |
| `ChartFrameCapture.cs` | 定义统一捕获接口、后端工厂、摄像机捕获和共享 GPU readback 帧。 |
| `ChartGameViewFrameCapture.cs` | 在帧末按当前游戏分辨率捕获最终游戏画面并异步读回。 |
| `ChartUnityAudioCapture.cs` | 使用 Unity `AudioRenderer` 离线捕获音频并写成 float32 WAV。 |
| `FfmpegEncoder.cs` | FFmpeg rawvideo pipe、GPU/CPU 编码选择、视频完成、音频 mux。 |
| `ChartRenderVisualClock.cs` | 强制视觉时间轴，Patch conductor 的 `songposition_minusi` 和 `calibration_i`。 |
| `ChartRenderAutoPlayer.cs` | 渲染期间自动补打砖块，并 suppress 异步输入角度修正。 |
| `ChartRenderAudioPatches.cs` | 屏蔽界面音效进入成品音频。 |
| `ChartRenderJudgmentPatches.cs` | 根据设置隐藏或显示判定文字。 |
| `ChartRenderDiagnostics.cs` | 写 `render.log`，记录球抽搐、跳块、失败、FFmpeg 等诊断信息。 |
| `ChartRenderPaths.cs` | FFmpeg 路径、工作区路径、导出路径、文件名清理。 |
| `ChartRenderResult.cs` | 渲染结果 DTO。 |

## 入口

渲染由 `EditorTweaksOverlayWindow.StartChartRender()` 启动：

```text
chartRenderSession = new ChartRenderSession(Main.Mod, Main.Settings)
StartCoroutine(chartRenderSession.Run(callback))
```

渲染是否可用由 `ChartRenderSession.IsPlayableLevelLoaded()` 和 `HasRenderableAudio()` 判断。

编辑器环境：

- `ADOBase.editor != null`
- `editor.customLevel != null`
- `editor.customLevel.levelData != null`
- `editor.floors.Count > 1`

非编辑器环境：

- `ADOBase.controller != null`
- `ADOBase.controller.gameworld == true`
- `ADOBase.lm.listFloors.Count > 1`
- 如果 `ADOBase.customLevel != null`，则要求它不在 loading 且有 `levelData`。

这个判断允许官谱直场景，因为官谱可能没有 `ADOBase.customLevel`，但仍有 `scrController`、`scrLevelMaker` 和 floors。

## 主流程

`ChartRenderSession.Run()` 的顺序：

1. 设置 `IsActive = true` 和 `IsRendering = true`。
2. `TryPrepare()`：
   - 补全设置默认值。
   - 创建 workspace 和 export 目录。
   - 清空并重建 `CurrentRender`。
   - 设置 `temp_video.mp4`、`audio.wav`、最终输出路径。
   - 开启 `ChartRenderDiagnostics.Begin(render.log)`。
   - 根据设置解析渲染范围：整首谱面，或编辑器当前选中的连续砖块段落。
3. `TryStartPlayback()`：
   - `ChartRenderPlaybackController` 保存旧状态。
   - 设置 `Time.captureFramerate` 为目标 FPS。
   - `QualitySettings.vSyncCount = 0`。
   - `Application.targetFrameRate = max(1000, fps * 4)`。
   - 编辑器走 `StartEditorPlayback()`，整首从第 0 块开始，片段从选中段落起点开始。
   - 非编辑器走 `StartGameScenePlayback()`。
4. 等待播放 schedule：
   - conductor 已存在。
   - `hasSongStarted` 为 true，或 controller state 是 `Countdown` / `PlayerControl`。
5. `BeginForcedVisualClock()`：
   - 记录当前 `songposition_minusi` 作为视觉锚点。
   - 记录 pitch、addoffset、当前 input offset 到日志。
6. 初始化：
   - 估算总时长。
   - 创建 `ChartRenderMemoryBudget`。
   - 创建 `ChartRenderFramePipeline`。
   - 根据已锁定的画面捕获方式创建摄像机或游戏画面后端。
   - 创建并启动 `ChartUnityAudioCapture`。
   - 创建 `FfmpegEncoder` 并 `BeginVideo()`。
7. 主循环：
   - 等 GPU readback 队列有空位。
   - `yield return new WaitForEndOfFrame()`。
   - `audioCapture.CaptureFrame()`。
   - `frameCapture.RequestFrame(requestedFrames)`。
   - `SetForcedFrameTime(requestedFrames + 1)`。
   - `ChartRenderFramePipeline.DrainReadyFrames(encoder)`。
   - 检测关卡结束并切换到尾巴帧数。
8. Drain 剩余 GPU 帧。
9. 完成音频、恢复状态。
10. 后台线程执行 `encoder.CompleteVideo()`。
11. 后台线程执行 `encoder.MuxAudioFile(capturedAudioPath)`。
12. 成功后保留临时目录，失败或取消时按路径删除。
13. `Finish()` 清理 `IsRendering`、视觉时钟、诊断日志并回调 UI。

## 渲染范围

默认渲染整首谱面，保持最稳定的第 0 格启动路径。

开启 `ChartRenderUseSelectedRange` 后，只在编辑器中生效。使用方式：

- 在编辑器里框选至少两个连续砖块。
- 渲染器读取选中范围的最小 `seqID` 和最大 `seqID`。
- 官方播放路径从最小 `seqID` 开始。
- 启动后先等待官方 checkpoint 从 `States.Checkpoint` 切入 `States.PlayerControl`，再开始捕获画面和音频。
- 当 controller、当前 floor 或玩家 floor 到达最大 `seqID` 后立即停止，不使用 `ChartRenderCompletionTailSeconds`。

片段渲染的关键保护是 `ChartRenderSession.AutoPlaybackEndFloor`。`ChartRenderAutoPlayer` 在命中前会检查 `current.nextfloor.seqID`，超过片段终点就不再补打。`scrPlayer.Hit` 也会阻止任何越过片段终点的命中。这样尾巴录制期间不会继续打到选区后面的砖块。

不能在 `States.Checkpoint` 阶段立刻开始捕获。官方 checkpoint 会把音乐音量设为 0，并在接近 checkpoint 地板时淡入；如果此时开始录制，成品会出现球停在起点、音频从小到大、视频时长超过选区的问题。

片段输出文件名会追加范围后缀，例如：

```text
SongName_f32-f64_yyyyMMdd_HHmmss.mp4
```

## 播放启动策略

### 编辑器

`StartEditorPlayback()` 使用官方编辑器播放路径。整首渲染时起点是第 0 块，片段渲染时起点是选中段落的第一个砖块：

```text
editor.SelectFloor(editor.floors[startFloor], cameraJump: false)
GCS.checkpointNum = startFloor
RDC.auto = false
editor.Play()
RDC.auto = false
```

先 `RDC.auto = false` 是为了让官方 `editor.Play()` 按正常路径初始化。真正的 `RDC.auto = true` 要等 `BeginForcedVisualClock()` 锚定视觉时钟后才开启，同时设置 `ChartRenderSession.IsAutoPlaybackReady = true`。这是防止开局跳砖块和一帧追打一串砖块的关键保护。

### 游戏 / 官谱 / scnGame

`StartGameScenePlayback()` 会记录场景信息到日志：

```text
scene=<ADOBase.sceneName> level=<controller.levelName> scnGame=<ADOBase.customLevel != null> state=<controller.state>
```

如果播放已经 schedule，就直接捕获当前时间线。否则：

- `GCS.checkpointNum = 0`
- `AbortWaitingForStartCoroutine(controller)`：通过私有字段 `waitForStartCoCallCount` 让等待开始协程失效。
- 隐藏 Press To Start。
- 显示 Get Ready。
- `ADOBase.conductor.Rewind()`
- `ADOBase.conductor.Start()`
- `controller.Start_Rewind(checkpoint)`
- 自定义 `scnGame` 再调用 `FinishCustomLevelLoading(checkpoint)`

官谱旧场景的关键是不要依赖 `ADOBase.customLevel`，而是依赖已有 controller 和 floors。

## 定帧与视觉时钟

只设置 `Time.captureFramerate` 不够。新版游戏的 conductor、输入校准、异步输入角度修正仍可能影响视觉结果，造成：

- 视觉和音频有很小相位差。
- 球在某些砖块突然抽搐。
- 自动播放一次追过多个砖块。

当前定帧设计：

```text
forcedSongPosition = startSongPosition + outputFrameIndex / fps * song.pitch
```

`startSongPosition` 必须在播放 schedule 后读取，因为此时官方 countdown、起点和场景初始化已经完成。

Patch 点：

- `scrConductor.set_songposition_minusi` Prefix：
  - 如果 `ChartRenderVisualClock.TryGetSongPosition()` 成功，把 setter 的 `value` 替换成 forced song position。
- `scrConductor.get_calibration_i` Prefix：
  - 渲染期间直接 `__result = 0f` 并 `return false`。

为什么要去掉 `calibration_i`：

- 玩家输入偏移是为了游玩手感，不应该把离线视频画面再整体挪一个相位。
- 音频来自 Unity mixer 的离线输出，不需要靠输入偏移修正。
- 之前视觉起点和音频起点很小相位差就是由这里暴露出来的。

## 自动打击与防球抽搐

`ChartRenderAutoPlayer.CatchUp()` 在 `scrConductor.Update` Postfix 中执行。

判断条件：

- 正在渲染。
- 自动播放已经允许，`ChartRenderSession.IsAutoPlaybackReady == true`。
- 视觉时钟活跃。
- controller 不为空。
- controller 没暂停。
- state 是 `PlayerControl`。
- playerManager 存在。

对每个 active player：

1. 读取当前 floor 和 nextfloor。
2. 刷新 chosen planet 角度。
3. 如果 `conductor.songposition_minusi + tolerance >= nextfloor.entryTime`，就应该命中。
4. 调用 `HitPerfect()`。

`HitPerfect()` 做：

- 暂时设置 `RDC.auto = true`。
- `controller.responsive = true`。
- 清理 paused、multipress penalty、multipress first press。
- 清空 `player.keyTimes`。
- 非 midspin 时把 planet `angle` 和 `cachedAngle` 对齐到 `targetExitAngle`。
- hold tile 先调用 `holdRenderer.Hit()`。
- 调用 `player.Hit(isAuto: true)`。
- finally 恢复旧的 `RDC.auto`。

防线：

- `MaxHitsPerFrame = 16`，防止异常情况下无限追块。
- 每次失败、跳块、达到 guard 都写入 `render.log`。
- 片段渲染时，Patch `scrPlayer.Hit` 阻止任何命中越过选中段落终点。
- Patch `AsyncInputUtils.AdjustAngle(scrPlayer, ulong)`：渲染时直接跳过，并记录 suppressed 次数。

如果球又抽搐，优先看 `render.log`：

- `AUTO_HIT ... FLOOR_JUMP`
- `AUTO_HIT_GUARD_REACHED`
- `PLAYER_FAILED`
- `SONG_MOVED_BACKWARD`
- 最后一行 diagnostics summary 的 `failedAutoHits` 和 `floorJumps`

## 画面捕获

渲染会话创建时会锁定 `ChartRenderCaptureSource`，渲染途中修改设置只影响下一次任务。两个后端都实现 `IChartFrameCapture`，并输出相同尺寸和像素格式的 `ChartPendingFrame`，因此共用 GPU readback 队列、重复帧逻辑和 FFmpeg 管线。

### 摄像机渲染

摄像机模式是默认值。`ChartCameraFrameCapture` 不自己新建摄像机，而是使用官方 `scrCamera.instance`：

- `Bgcamstatic`
- `BGcam`
- `camobj`

它保存旧 target：

- `oldBgStaticTarget`
- `oldBgTarget`
- `oldMainTarget`
- `oldOverlayActive`
- `oldQuadActive`
- `oldQuadTexture`

然后把三台相机都指向同一个 `RenderTexture(width, height, 24, ARGB32)`。

为什么使用官方相机链：

- 官谱和旧官谱场景的摄像机层级不一定和自定义谱一致。
- 背景、滤镜、视频背景、overlay quad 都由官方 `scrCamera` 管。
- 自建相机容易漏掉后处理或 depth 顺序。

读回默认：

```text
AsyncGPUReadback.Request(captureTarget, 0, TextureFormat.RGBA32)
```

高级设置可以切到实验性的 `TextureFormat.BGRA32`。如果当前 Unity runtime 不支持 BGRA，会记录日志并回退 RGBA。

完成后 `request.GetData<byte>()` copy 到复用的 byte[]，交给 `ChartRenderFramePipeline` 再写入 FFmpeg writer。

### 游戏画面渲染

兼容模式不修改任何摄像机的 `targetTexture`。每个需要新画面的输出帧在 `WaitForEndOfFrame` 后执行：

```text
ScreenCapture.CaptureScreenshotIntoRenderTexture(captureTarget)
    -> AsyncGPUReadback(captureTarget)
    -> 现有 ChartRenderFramePipeline
```

源纹理和成品尺寸固定为开始渲染时的 `Screen.width x Screen.height`，不读取摄像机模式保存的输出宽高，也不创建缩放或留边用的中间纹理。为了兼容 `yuv420p`，极少数奇数宽高窗口会由 FFmpeg 在右侧或底部补最多一个黑色像素。

这个模式会捕获额外摄像机、游戏 UI、编辑器 UI、IMGUI 和屏幕空间 Canvas。EditorTweaks 自己的主浮窗和进度遮罩会在整个捕获期间隐藏，`Esc` 用于取消；输入保护仍保持生效，按键不会传给暂停或游玩逻辑。摄像机预览的 `Overlaycam + quad` 不会启用，避免递归画面。

渲染期间如果 `Screen.width` 或 `Screen.height` 改变，会立即终止本次任务并走既有清理和播放状态恢复流程，不会在存在 pending GPU readback 时重建纹理。`render.log` 会记录捕获模式、游戏分辨率、回读格式、垂直翻转策略和屏幕捕获异常。

摄像机 RenderTexture 的原始回读需要 FFmpeg `vflip`；`ScreenCapture` 的回读方向已经与视频输入一致，因此游戏画面模式不应用 `vflip`。两个后端通过 `IChartFrameCapture.RequiresVerticalFlip` 分别声明方向，避免重复翻转。

## 内存预算与队列

渲染瓶颈主要来自 GPU readback、CPU 拷贝、raw frame pipe 和编码器吞吐。不能让队列按固定帧数无限堆，所以现在按分辨率计算预算：

| 分辨率级别 | 单帧 RGBA 估算 | 默认缓存预算 | GPU readback pending |
| --- | ---: | ---: | ---: |
| 1080p | 约 7.9 MiB | 384 MiB | 最多 8 帧 |
| 1440p | 约 14.1 MiB | 512 MiB | 最多 6 帧 |
| 4K | 约 31.6 MiB | 512 MiB | 最多 4 帧 |
| 8K | 约 126.6 MiB | 768 MiB | 最多 2 帧 |

FFmpeg 写入队列也按 `width * height * 4` 换算最大缓存帧数。队列满时会阻塞渲染推进，输出时间轴不变，只是等待更久。

注意：

- FFmpeg 只为摄像机模式应用 `vflip`；游戏画面模式保持原方向。
- Dispose 必须恢复所有 targetTexture、quad texture 和 active 状态。

## 音频捕获

`ChartUnityAudioCapture` 使用 Unity `AudioRenderer`：

1. `Begin()`：
   - 创建 WAV 文件。
   - 写 placeholder WAV header。
   - `AudioRenderer.Start()`。
2. 每帧 `CaptureFrame()`：
   - `AudioRenderer.GetSampleCountForCaptureFrame()`。
   - 按 speaker mode 推导 channel count。
   - `AudioRenderer.Render(samples)`。
   - 写 float32 PCM 数据。
3. `Complete()`：
   - 回到文件头重写 RIFF/WAVE header。
4. `Dispose()`：
   - `AudioRenderer.Stop()`。
   - 释放 NativeArray 和 stream。

这个方式的优点：

- 不需要手工混合歌曲、打拍音、hold 音效、PlaySound。
- pitch、音量、mixer、游戏实际播放时序都由 Unity 负责。

Patch `scrSfx.PlaySfx(... InterfaceParent ...)` 的原因：

- UMM 点击、菜单、界面音也在 Unity mixer 里。
- 渲染成品不应该包含这些声音。
- 只屏蔽 `MixerGroup.InterfaceParent`，谱面音效不屏蔽。

## FFmpeg

`FfmpegEncoder` 的视频命令输入默认是 raw RGBA，实验模式可切到 raw BGRA：

```text
-f rawvideo
-pixel_format rgba|bgra
-video_size <width>x<height>
-framerate <fps>
-i -
-an
-vf <camera: vflip,pad | game-view: pad>
<encoder args>
-pix_fmt yuv420p
temp_video.mp4
```

编码档位：

| 档位 | NVENC | x264 回退 | 用途 |
| --- | --- | --- | --- |
| Auto Balanced | `h264_nvenc p4` | `veryfast` | 默认推荐，优先 GPU，兼顾速度和质量。 |
| Fastest | `h264_nvenc p1` | `ultrafast` | 最快出片。 |
| Balanced | `h264_nvenc p4` | `veryfast` | 手动指定均衡。 |
| Quality | `h264_nvenc p6` | `fast` | 更慢，但压缩质量更稳。 |
| CPU Compatibility | 不使用 | `veryfast` | 硬件编码失败或兼容性排查。 |
| Custom | 兼容旧逻辑 | `cpu` / `x264:<preset>` | 高级手动兜底。 |

默认输出仍是 H.264 + yuv420p + AAC + MP4 + faststart，播放器和投稿兼容性主要来自这些封装和格式，而不是 `fast` / `veryfast` 名称本身。

## 码率限制

视频编码会设置目标码率、最大码率和缓冲区：

```text
-b:v <target>M
-maxrate <target * 1.5>M
-bufsize <target * 2>M
```

NVENC 使用 VBR：

```text
-rc vbr -cq <quality> -b:v <target>M -maxrate <max>M -bufsize <buffer>M
```

x264 使用单次 ABR：

```text
-b:v <target>M -maxrate <max>M -bufsize <buffer>M
```

`ChartRenderBitrateMbps = 0` 表示自动推荐。60fps 常见推荐：

| 分辨率 | 推荐码率 |
| --- | ---: |
| 1080p | 20 Mbps |
| 2K / 1440p | 35 Mbps |
| 4K | 60 Mbps |

码率限制会明显降低 4K 文件体积，也能避免播放器遇到过高瞬时码率时卡顿。代价是极复杂画面在低码率下会更容易出现压缩痕迹。

音频 mux：

```text
-i temp_video.mp4
-i audio.wav
-map 0:v:0
-map 1:a:0
-c:v copy
-c:a aac
-b:a 320k
-ac 2
-movflags +faststart
final.mp4
```

如果高级设置里的音频同步偏移不为 0，mux 阶段会额外加 audio filter：

- 正数：音频提前。用 `atrim=start=<seconds>,asetpts=PTS-STARTPTS` 裁掉音频开头，让后面的声音更早对上画面。
- 负数：音频延后。用 `adelay=<ms>:all=1,asetpts=PTS-STARTPTS` 给音频补延迟。

这个设置是给固定偏移环境兜底的，不参与游戏时间轴，也不会影响球、滤镜或自动打击。

之前 FFmpeg mux exit code `-22` 的常见原因是输出文件名带非法字符或路径异常。`ChartRenderPaths.MakeSafeFileName()` 会：

- 去掉富文本 tag。
- 替换 Windows 非法文件名字符。
- 合并空白和下划线。
- trim 空格、点、下划线。
- 限制基础文件名长度。

## 取消和恢复

`Cancel()` 只设置 `cancelRequested = true`。主协程和后台线程会在安全点检查。

恢复状态由 `ChartRenderPlaybackController` 内部的状态快照负责：

- `RDC.auto`
- `GCS.checkpointNum`
- `Time.captureFramerate`
- `Application.targetFrameRate`
- `QualitySettings.vSyncCount`
- 编辑器选中 floor

编辑器环境还会在必要时调用 `editor.SwitchToEditMode()`。这个恢复在 `Finish()` 清掉 `IsRendering` 后还会再执行一次，避免渲染模态输入遮罩阻止切回编辑模式。

## 踩坑记录

- 不要在渲染模态窗口期间跳过 `scrController.Update`。控制器更新是画面和状态推进的一部分。
- 不要在播放 schedule 之前锚定视觉时钟，否则起点相位可能错。
- 不要把玩家输入偏移叠到渲染视觉上，渲染不是实时游玩。
- 不要用旧版 `scrConductor` 兼容分支覆盖新版逻辑。
- 不要手工拼音频，Unity `AudioRenderer` 更稳定。
- 不要自建相机链，官方 `scrCamera` 三相机链对官谱更可靠。
- 不要忽略 `render.log`。球抽搐类问题先看日志再改逻辑。
