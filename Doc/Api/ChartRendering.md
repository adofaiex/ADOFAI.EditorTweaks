# 谱面渲染公共 API

ADOFAI Editor Tweaks 从 API v1 开始提供强类型谱面渲染任务接口。任何 UnityModManager Mod 都可以引用发布包中的 `ADOFAI.EditorTweaks.dll` 并调用，不需要访问浮窗、`Settings`、Harmony 或内部渲染类。

公共命名空间：

```csharp
using ADOFAI.EditorTweaks.Api.Rendering;
```

调用方应把 `ADOFAI.EditorTweaks` 声明为运行依赖，并在 Unity 主线程调用 `ChartRenderApi.Start`。低层摄像机、ScreenCapture、AudioRenderer 和 FFmpeg 类型不是公共 API。

## 版本与可用性

```csharp
int version = ChartRenderApi.ApiVersion; // 1
ChartRenderAvailability availability = ChartRenderApi.GetAvailability();
if (!availability.Available)
{
    // availability.ErrorCode / availability.Message
}
```

API v1 内只增加成员，不修改已有签名、枚举数值或语义。Mod 被停用、Chart Rendering 补丁组不兼容或渲染服务尚未创建时，`Available` 为 false。

## 基本流程

```csharp
ChartRenderRequest request = ChartRenderApi.CreateRequestFromCurrentSettings();
request.PlaybackMode = ChartRenderPlaybackMode.RendererControlled;
request.ShowBuiltInProgressUi = true;

ChartRenderStartResult start = ChartRenderApi.Start(request);
if (!start.Success || start.Task == null)
{
    UnityEngine.Debug.LogError(start.ErrorCode + ": " + start.Message);
    return;
}

ChartRenderTask task = start.Task;
task.Completed += (_, result) =>
{
    if (result.Success)
    {
        UnityEngine.Debug.Log("Rendered: " + result.OutputPath);
    }
    else
    {
        UnityEngine.Debug.LogError(result.ErrorCode + ": " + result.Message);
    }
};
```

`Start` 接受请求后会在下一帧启动内部协程，因此调用方可以在返回后立即订阅事件。每次启动都会复制整个请求；之后修改原请求不会影响任务，也不会修改玩家保存的 EditorTweaks 设置。

同时只能运行一个任务。`ChartRenderApi.CurrentTask` 只在任务非终态时返回当前任务；调用方应保留自己获得的 `ChartRenderTask` 引用，以便完成后读取结果。

## 请求字段

| 字段 | 说明 |
| --- | --- |
| `CaptureSource` | `Camera` 或 `GameView`。 |
| `PlaybackMode` | 渲染器自动播放、重新开始但不自动打击、附着当前播放。 |
| `Range` | 整首、当前编辑器选区、明确砖块范围、当前播放到结尾。 |
| `Width` / `Height` | 摄像机模式输出尺寸，必须为 16–7680 / 16–4320 范围内的偶数。 |
| `FramesPerSecond` | 1–240。 |
| `WorkspaceDirectory` | 临时视频、音频和 `render.log` 所在目录。 |
| `OutputDirectory` | 最终视频目录。 |
| `OutputFileName` | 可选基础文件名；不允许目录分隔符，扩展名由 `VideoFormat` 决定。 |
| `VideoFormat` | MP4、MKV、MOV。 |
| `AudioFormat` | AAC、FLAC、ALAC。 |
| `EncoderMode` | 自动均衡、最快、均衡、质量、CPU 兼容或自定义。 |
| `ReadbackFormat` | RGBA 或实验性 BGRA。 |
| `PreviewMode` | 完整、暗色、极简；只影响摄像机模式。 |
| `Quality` | 0–51。 |
| `BitrateMbps` | 0–300；0 表示自动推荐。 |
| `CompletionTailSeconds` | 自然结束或主动完成后继续捕获的秒数。 |
| `AudioSyncOffsetMilliseconds` | -5000 到 5000。 |
| `ShowHitJudgments` | 是否把判定文字录入成品。 |
| `ShowBuiltInProgressUi` | 是否显示 EditorTweaks 的进度和取消界面。 |
| `CustomEncoderPreset` / `CustomMuxArguments` | 专业兼容设置。 |

游戏画面模式始终输出任务初始化时的 `Screen.width × Screen.height`。请求中的 `Width`、`Height` 不参与该模式的捕获；实际值通过 `ChartRenderTask.OutputWidth` 和 `OutputHeight` 获取。

输出文件已存在时不会覆盖，会自动追加数字后缀。

## 范围

```csharp
request.Range = ChartRenderRangeRequest.WholeLevel();
request.Range = ChartRenderRangeRequest.CurrentEditorSelection();
request.Range = ChartRenderRangeRequest.Floors(100, 240);
request.Range = ChartRenderRangeRequest.CurrentPlaybackToEnd();
```

- 编辑器选区必须是至少两个砖块组成的有效连续范围。
- 明确范围要求 `start >= 0`、`end > start` 且没有超出当前谱面。
- `CurrentPlaybackToEnd` 只允许与 `AttachToCurrentPlayback` 一起使用。
- 附着模式以 API 调用时的当前玩家位置作为范围起点。

## 播放控制

### RendererControlled

保持内置浮窗行为。渲染器重新开始指定范围，启用固定视觉时钟和内部自动打击。

### RestartWithoutAutoPlay

渲染器负责重新开始谱面和固定时间轴，但 `RDC.auto` 与 EditorTweaks 自动打击保持关闭。外部 Mod 应在调用 API 前准备好自己的播放逻辑，并通过正常游戏 Hook 或程序调用推进玩家。

```csharp
ChartRenderRequest request = ChartRenderApi.CreateRequestFromCurrentSettings();
request.PlaybackMode = ChartRenderPlaybackMode.RestartWithoutAutoPlay;
request.Range = ChartRenderRangeRequest.WholeLevel();
request.ShowBuiltInProgressUi = false;

ChartRenderStartResult start = ChartRenderApi.Start(request);
```

渲染期间真实键鼠输入仍会被输入保护拦截，避免人工操作污染外部控制结果。

### AttachToCurrentPlayback

不调用编辑器 Play、不 rewind、不修改当前砖块。调用前必须已经进入倒计时或播放状态。

```csharp
ChartRenderRequest request = ChartRenderApi.CreateRequestFromCurrentSettings();
request.PlaybackMode = ChartRenderPlaybackMode.AttachToCurrentPlayback;
request.Range = ChartRenderRangeRequest.CurrentPlaybackToEnd();
request.ShowBuiltInProgressUi = false;

ChartRenderStartResult start = ChartRenderApi.Start(request);
if (start.Success && start.Task != null)
{
    ChartRenderTask task = start.Task;
    // 在需要提前结束并保留视频时调用。
    task.RequestFinish();
}
```

附着任务只恢复渲染器修改的捕获帧率、目标帧率、垂直同步和临时资源，不强制把编辑器切回编辑状态。

## 状态、进度与结束

```text
Preparing
-> WaitingForPlayback
-> Rendering
-> Finalizing
-> Completed | Failed | Canceled
```

`ChartRenderTask.Progress` 是不可变快照，包含总帧、已写帧、重复帧、处理速度、预计剩余时间、当前阶段、编码器、内存和队列摘要。

`ProgressChanged` 最多约每 100 ms 触发一次，终态前一定会再发布最终快照。所有事件都在 Unity 主线程执行；某个订阅者抛出异常时只写入 UMM 日志。

```csharp
task.ProgressChanged += currentTask =>
{
    ChartRenderProgress progress = currentTask.Progress;
    UnityEngine.Debug.Log($"{progress.Value:P1} ({progress.WrittenFrames}/{progress.TotalFrames})");
};
```

正常停止：

```csharp
bool accepted = task.RequestFinish();
```

只有 `Rendering` 状态接受主动完成。渲染器在下一个安全帧记录结束点，继续捕获 `CompletionTailSeconds`，然后生成最终文件。

取消：

```csharp
task.Cancel();
```

取消可以从任意线程请求，未完成的临时文件会清理，不生成成品。`RequestFinish` 也使用线程安全标记。

## 启动错误

| 错误码 | 含义 |
| --- | --- |
| `ApiUnavailable` | Mod 未启用、服务未创建或渲染补丁组不可用。 |
| `WrongThread` | `Start` 不是从 Unity 主线程调用。 |
| `Busy` | 已有全局渲染任务。 |
| `InvalidRequest` | 枚举、范围、尺寸、路径或数值不合法。 |
| `NoPlayableLevel` | 当前没有可播放谱面。 |
| `NoRenderableAudio` | 当前谱面没有可捕获的音频。 |
| `PlaybackNotActive` | 附着模式下播放尚未开始。 |
| `InitializationFailed` | 捕获器、工作目录、FFmpeg 或播放启动初始化失败。 |
| `RenderingFailed` | 捕获、编码或合成期间失败。 |
| `Canceled` | 调用方取消任务。 |
| `ModDisabled` | 渲染期间 EditorTweaks 被停用。 |

预期错误通过 `ChartRenderStartResult` 或最终 `ChartRenderResult` 返回，不要求调用方解析本地化 UI 文本。完整异常仍写入 UMM 日志和 `render.log`。
