# VideoBackgroundSync 模块

`src/Features/VideoBackgroundSync` 修复从中途播放谱面时视频背景不同步的问题。

## 问题背景

官方视频背景通常在 `VideoPlayer.Prepare()` 完成后设置一次 `VideoPlayer.time` 并播放。对于较长视频，中途 seek 的解码准备可能晚于谱面播放，尤其在：

- 编辑器从选中地板播放。
- 从 checkpoint 播放。
- 视频较长或码率较高。
- 机器负载较高。

表现是视频背景比音乐和谱面晚一点，或者启动后短时间漂移。

## Patch

| 目标方法 | 类型 | 作用 |
| --- | --- | --- |
| `scrVfxPlus.Reset` | Postfix | 删除该 VFX 实例的同步状态。 |
| `scrVfxPlus.Update` | Postfix | 检查并校正视频背景时间。 |

## 同步条件

`TrySynchronize(scrVfxPlus vfx)` 会在以下条件不满足时直接返回：

- 功能开关 `EnableVideoBackgroundSyncFix` 为 true。
- `vfx.videoBG != null`。
- `ADOBase.conductor != null`。
- `ADOBase.controller != null`。
- controller 未暂停。
- conductor 已开始播放。
- video gameObject active。
- video 已 prepared。
- `Persistence.visualEffects == VisualEffects.Full`。

## 目标时间

目标视频时间：

```text
songposition_minusi - countdownOffset + vidOffset
```

其中：

```text
countdownOffset = conductor.separateCountdownTime
    ? conductor.crotchetAtStart * conductor.adjustedCountdownTicks
    : 0
```

`vidOffset` 来自 `scrVfxPlus.vidOffset`。

如果目标时间小于 0，说明还没到视频该开始的时间，不同步。

如果能读取视频长度：

- 循环视频：`targetTime %= length`
- 非循环视频：clamp 到 `length - 0.001`

## 启动窗口

状态按 `scrVfxPlus.GetInstanceID()` 存在字典里。

当检测到：

- video 刚开始 playing。
- 或 `vfx.hasPlayed` 刚被标记。

就进入启动同步窗口：

```text
StartupSyncFrames = 150
StartupSeekAttempts = 0
```

启动窗口内允许更积极地 seek：

- 软阈值：`0.08s`
- 最多：`12` 次
- cooldown：`0.04s`

运行期仍允许硬校正：

- 硬阈值：`0.35s`
- cooldown：`0.25s`

## 重新播放处理

如果 `video.isPlaying == false`，但 `vfx.hasPlayed == true` 且仍在启动窗口，说明官方可能标记了已播放但 video 实际没跑起来。此时：

```text
video.time = targetTime
video.playbackSpeed = conductor.song.pitch
video.Play()
```

## Pitch

每次同步都会：

```text
video.playbackSpeed = conductor.song.pitch
```

这样加速或变速播放时，视频背景跟随音乐 pitch。

## 离线渲染期间的帧同步

视频渲染使用 `Time.captureFramerate`，因此渲染期间会临时调整每个 `VideoPlayer`：

- `timeUpdateMode` 切换为 `VideoTimeUpdateMode.GameTime`，让视频跟随 Unity 捕获帧时钟。
- `skipOnDrop` 设为 `false`，避免主动丢视频帧造成抽帧感。
- 从头播放时只使用游戏原版的启动定位；接管已经播放中的场景时，Mod 最多补一次 `VideoPlayer.time` 定位。
- 启动后不再 seek、不动态调整 `playbackSpeed`，视频始终按谱面 pitch 连续播放。
- 检测到视频背景时将渲染处理速度限制到输出 FPS，避免离线渲染跑得比 WindowsVideoMedia 解码快。带视频背景的导出速度会更接近实时。

渲染结束或失败时会恢复 VideoPlayer 原来的 `timeUpdateMode` 和 `skipOnDrop` 设置，不改变正常游戏播放行为。

## 踩坑记录

- 不能每帧无条件 seek，长视频会卡顿或反复 prepare。
- 启动阶段和运行阶段需要不同阈值。
- `video.length` 可能是 0，不能直接取模。
- 循环和非循环视频的边界处理不同。
- 只在 Full visual effects 下处理，避免低特效模式中干预不该播放的视频。
