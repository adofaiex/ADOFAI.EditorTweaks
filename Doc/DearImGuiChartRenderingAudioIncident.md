# Dear ImGui 导致视频渲染无声的事故报告

## 结论

不要在本项目中继续尝试接入 Dear ImGui，也不要用 Dear ImGui 替换当前编辑器覆盖窗口。该结论只针对本项目当前架构：ADOFAI 编辑器内视频渲染、Unity `AudioRenderer` 离线音频捕获、`Time.captureFramerate` 定帧渲染、`WaitForEndOfFrame` 画面采集以及游戏自身基于 `AudioSettings.dspTime` 的播放调度同时工作时，Dear ImGui 的 Unity Canvas 接入方式会破坏渲染视频的音频捕获。

本次故障不是 FFmpeg 参数问题，不是音频格式问题，不是 mux 阶段丢音轨，也不是谱面只有选中段落渲染才触发的问题。整首渲染和选中段落渲染都会出现，因为它们共用同一条 `ChartRenderSession.Run()` 渲染主循环。

本次已经实测确认：

- `1.2.7` 标签版本可以正常渲染视频，并且视频有声音。
- 引入 Dear ImGui UI 的提交 `9f538d9` 后，渲染视频无声音。
- 故障日志显示 Unity 音频捕获输出为 0 个采样，生成的 `audio.wav` 只有 44 字节 WAV 文件头。
- 临时停止渲染期间的 ImGui Canvas 更新后，视频声音恢复。
- 最终项目已回退到 `1.2.7`，远端 `origin/main` 也已回退到 `1.2.7`。

## 故障表现

用户点击“渲染视频”后，渲染流程可以继续推进，画面帧也可以写入，自动打击也可以执行，最终 FFmpeg 也会生成视频文件。但是生成的视频没有声音。

关键日志如下：

```text
Audio capture complete. samples=0 audioSeconds=0 videoFrames=1628 videoSeconds=13.566667 deltaAudioMinusVideo=-13.566667.
Muxing captured audio. videoBytes=77077472 audioBytes=44 audioSyncOffsetMs=158
```

这里的 `samples=0` 是最关键证据。它说明 Unity 音频捕获阶段没有拿到任何音频采样。

`audioBytes=44` 同样关键。标准 WAV 文件头刚好是 44 字节，这说明生成的 `audio.wav` 只有文件头，没有实际音频数据。也就是说，FFmpeg mux 时确实拿到了一个音频文件，但这个音频文件本身就是空的。

因此问题发生在 Unity 内部音频捕获阶段，而不是 FFmpeg 合成阶段。

另一个表现是渲染开始后画面一直显示“预备”，没有出现正常的 `3, 2, 1` 倒计时，也听不到倒计时打拍音。但谱面本身仍然会向前推进。

这个现象和游戏主逻辑也能对应上：

- `scrCountdown.Update()` 直接读取 `AudioSettings.dspTime` 来判断显示“预备 / 3 / 2 / 1 / GO”。
- `scrConductor.Update()` 在 `AudioSettings.dspTime` 不推进时，会用 `Time.unscaledTimeAsDouble` 做自己的时间补偿，所以谱面视觉和自动打击仍可推进。
- Unity `AudioRenderer.Start()` 进入音频捕获模式后会影响实际音频输出，并且这次故障中 `GetSampleCountForCaptureFrame()` 始终返回 0。

所以用户看到的“倒计时卡在预备，但谱面正常走”并不矛盾：倒计时 UI 和谱面主逻辑用的时间来源不同。

## 引入故障的变更范围

对比 `1.2.7` 到故障提交 `9f538d9`，`src/Features/ChartRendering` 目录没有发生变化。也就是说，以下关键渲染代码并没有被直接修改：

- `ChartUnityAudioCapture.cs`
- `ChartRenderSession.cs`
- `FfmpegEncoder.cs`
- `ChartRenderPlaybackController.cs`

真正发生变化的是编辑器覆盖窗口 UI：

- 旧版 `1.2.7` 使用 Unity IMGUI，也就是 `MonoBehaviour.OnGUI()`。
- 故障提交 `9f538d9` 引入 Dear ImGui，并新增 `EditorTweaksImGuiController`。
- Dear ImGui 的绘制结果不是直接显示在屏幕上，而是被转换为 Unity `Mesh`，再交给 Unity `CanvasRenderer` 绘制。
- 该实现创建了一个 `ScreenSpaceOverlay` 的 Unity `Canvas`，并在每帧 `Update()` 中持续更新。

故障提交中的关键实现包括：

```csharp
GameObject canvasObject = new GameObject("ADOFAI.EditorTweaks.ImGuiCanvas");
DontDestroyOnLoad(canvasObject);
canvas = canvasObject.AddComponent<Canvas>();
canvas.renderMode = RenderMode.ScreenSpaceOverlay;
canvas.sortingOrder = 32760;
```

以及每帧：

```csharp
ImGuiNET.ImGui.NewFrame();
OnLayout?.Invoke();
ImGuiNET.ImGui.Render();
UpdateCanvas();
```

`UpdateCanvas()` 内部会读取 Dear ImGui 的 draw data，生成 Unity `Mesh`，然后：

```csharp
CanvasRenderer renderer = GetRenderer(commandCount);
renderer.SetMaterial(material, fontTexture);
renderer.SetColor(Color.white);
renderer.SetMesh(mesh);
```

这意味着 Dear ImGui 本身虽然不是 Unity UI，但本项目中的接入方式把 Dear ImGui 的结果变成了真正参与 Unity UI 渲染管线的 `CanvasRenderer` 对象。

## 为什么 Dear ImGui 会影响视频渲染音频

本项目的视频渲染不是简单录屏，而是一套离线定帧渲染流程。

渲染时主要流程如下：

1. 使用游戏官方播放逻辑启动谱面。
2. 设置 `Time.captureFramerate`，让 Unity 按固定帧率推进。
3. 创建 `ChartUnityAudioCapture`。
4. 调用 `AudioRenderer.Start()` 进入 Unity 音频捕获模式。
5. 每一帧等待 `WaitForEndOfFrame`。
6. 调用 `AudioRenderer.GetSampleCountForCaptureFrame()` 查询这一帧应该捕获多少音频采样。
7. 调用 `AudioRenderer.Render(samples)` 获取这一帧音频。
8. 同一帧捕获画面并写入视频编码队列。
9. 渲染结束后把临时视频和 `audio.wav` 交给 FFmpeg 合成。

这套流程对 Unity 的帧生命周期非常敏感，尤其是：

- `Time.captureFramerate`
- `WaitForEndOfFrame`
- `AudioRenderer.Start()`
- `AudioRenderer.GetSampleCountForCaptureFrame()`
- Unity UI / Canvas 的渲染阶段
- 游戏自身的 `AudioSettings.dspTime` 和 `PlayScheduled`

Dear ImGui 接入后，它不是只在普通编辑状态绘制 UI，而是在渲染视频期间也持续运行。也就是说，在 `ChartRenderSession.IsRendering == true` 的时候，ImGui Canvas 仍然每帧执行：

- 收集输入
- 开启 ImGui frame
- 生成 ImGui draw data
- 创建或更新 Unity Mesh
- 更新 CanvasRenderer
- 让 `ScreenSpaceOverlay` Canvas 参与 Unity UI 渲染

这改变了原本 `1.2.7` 可正常工作的渲染帧环境。渲染期间 Unity 音频捕获需要在固定帧节奏下为每个 capture frame 产生 sample count，但故障版本中 `AudioRenderer.GetSampleCountForCaptureFrame()` 实际每帧返回 0，导致 `ChartUnityAudioCapture` 没有任何音频数据可写。

`ChartUnityAudioCapture.CaptureFrame()` 的逻辑本身是：

```csharp
int sampleCount = AudioRenderer.GetSampleCountForCaptureFrame();
int floatCount = Math.Max(0, sampleCount * channelCount);
if (floatCount == 0)
{
    return;
}
```

所以一旦 `sampleCount` 一直是 0，就会每帧直接返回，最终 `audio.wav` 只留下头部。

本次故障中 `samples=0` 与 `audioBytes=44` 完全符合这个路径。

## 为什么不是 FFmpeg 的问题

FFmpeg 阶段日志已经显示音频文件被传入 mux：

```text
-i "...temp_video.mp4" -i "...audio.wav"
```

并且 `audioBytes=44` 说明 FFmpeg 输入的 `audio.wav` 文件存在，只是里面没有 PCM/float 音频数据。

如果是 FFmpeg 参数错误，通常会看到以下现象之一：

- `audio.wav` 有明显大于 44 字节的数据，但输出视频没有音轨。
- FFmpeg 日志报错或 mux 失败。
- 换音频编码格式后行为变化。

本次实际不是这样。本次音频文件在进入 FFmpeg 前已经是空数据，所以 FFmpeg 不是根因。

## 为什么不是选中段落渲染的问题

用户最初日志中是选中段落渲染，但随后确认整首渲染也没有声音。

这符合代码结构：

- 选中段落和整首渲染都使用 `ChartRenderSession.Run()`。
- 两者都使用 `ChartUnityAudioCapture`。
- 两者都调用 `AudioRenderer.Start()` 和 `CaptureFrame()`。
- 两者最终都由 `FfmpegEncoder.MuxAudioFile()` 合成音频。

因此只要 `AudioRenderer.GetSampleCountForCaptureFrame()` 返回 0，选中段落和整首都会无声。

## 为什么 `1.2.7` 没有问题

`1.2.7` 的覆盖窗口使用 Unity 原生 `OnGUI()` 绘制。这个 UI 不创建持续参与 Unity UI 渲染管线的 `ScreenSpaceOverlay Canvas`，也没有每帧把 ImGui draw data 转成 `CanvasRenderer` Mesh。

也就是说，`1.2.7` 渲染视频时的 UI 层更轻，它不会额外创建并维护一套 Unity Canvas 渲染对象。这个状态下，`AudioRenderer.GetSampleCountForCaptureFrame()` 可以正常返回采样数，`audio.wav` 可以写入实际音频数据。

故障提交虽然主要是“UI 改造”，但它改变的是 UI 的底层渲染方式，而不是只改颜色、布局或按钮样式。

这就是为什么表面看起来只是 UI 改动，却会影响视频渲染音频。

## 临时修复为什么有效

在排查过程中曾做过一个最小验证修复：

- 普通编辑状态继续使用 ImGui UI。
- 一旦 `ChartRenderSession.IsRendering == true`，立即停止 ImGui Canvas 更新。
- 渲染期间的进度浮层改回 `OnGUI()` 绘制。

测试结果：视频渲染声音恢复。

这说明问题不是 Dear ImGui 的某个按钮、某段文字、某个颜色、某个进度条控件，而是“渲染期间仍然持续运行的 ImGui Canvas UI 层”。

如果只隐藏进度条，但 ImGui Canvas 仍然每帧运行，风险仍然存在。

如果只隐藏主面板，但 ImGui 控制器仍然 `NewFrame()`、`Render()`、`UpdateCanvas()`，风险也仍然存在。

真正有效的是：渲染期间不能让这套 ImGui Canvas 管线参与 Unity 渲染。

## 最终处理

最终没有保留临时修复，而是按用户要求直接回退到 `1.2.7`。

当前处理结果：

- 本地 `main` 回退到 `1.2.7`。
- 远端 `origin/main` 回退到 `1.2.7`。
- 游戏目录中的 `ADOFAI.EditorTweaks` Mod 已重新部署为 `1.2.7`。
- 新增的 ImGui 相关运行文件已从游戏 Mod 目录清除，包括：
  - `cimgui.dll`
  - `ImGui.NET.dll`
  - `SourceHanSansSC-Bold.otf`

## 对后续开发者的明确警告

不要在本项目中再次尝试使用 Dear ImGui 重写编辑器覆盖窗口。

不要在视频渲染期间创建或更新以下 UI 管线：

- Dear ImGui
- ImGui.NET
- cimgui
- 每帧 `ImGui.NewFrame()` / `ImGui.Render()`
- 将 ImGui draw data 转成 Unity Mesh
- `CanvasRenderer.SetMesh`
- `ScreenSpaceOverlay` Canvas
- 渲染期间持续刷新的 Unity Canvas UI

特别不要恢复或重写以下结构：

```csharp
canvas = canvasObject.AddComponent<Canvas>();
canvas.renderMode = RenderMode.ScreenSpaceOverlay;
...
ImGuiNET.ImGui.Render();
UpdateCanvas();
...
renderer.SetMesh(mesh);
```

这些写法在普通 Unity 工具窗口里可能成立，但在本项目的离线视频渲染链路中已经被实测证明会导致音频捕获失败。

如果未来需要优化 UI，只允许在现有 `OnGUI()` 覆盖窗口基础上做小范围调整，或者先对视频渲染链路做完整隔离设计并经过专项验证。未经验证，不得引入 Dear ImGui 或任何类似的每帧 Canvas/Mesh UI 渲染桥接层。

## 判断类似问题的方法

如果未来又出现“视频渲染无声音”，优先检查 `render.log` 中这几项：

```text
Audio capture complete. samples=...
audioBytes=...
```

判断方式：

- `samples=0` 且 `audioBytes=44`：Unity 音频捕获阶段没有采样，重点查 `AudioRenderer`、帧生命周期、UI Canvas、`Time.captureFramerate`。
- `samples>0` 且 `audioBytes>44`：音频捕获已经有数据，再检查 FFmpeg mux 或音频编码参数。
- 倒计时一直显示“预备”但谱面推进：重点查 `AudioSettings.dspTime` 与 `scrConductor.dspTime` 是否分离。

本次事故属于第一种。

## 相关提交和版本

- 正常版本：`1.2.7`
- 故障提交：`9f538d9 Add ImGui-based editor tweaks for enhanced overlay functionality`
- 故障提交的主要变化：新增 ImGui.NET / cimgui 接入，新增 `EditorTweaksImGuiController`，将覆盖窗口改为 Dear ImGui + Unity CanvasRenderer 渲染。
- 最终处理：仓库和远端主分支回退到 `1.2.7`，保留本报告作为后续开发约束。
