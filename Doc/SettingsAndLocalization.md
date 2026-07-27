# Settings 与 Localization

本项目的用户设置集中在 `src/Settings.cs`，用户可见文本集中在 `Resources/localization.json`，运行时由 `src/Localization.cs` 读取。

## Settings 字段

### 功能开关

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `EnableNumericDrag` | true | 启用数值输入框右键拖动。 |
| `EnableCameraRelativeDecorationDragFix` | true | 启用 Camera / CameraAspect 装饰拖动修复。 |
| `EnableDecorationPivotFix` | true | 启用装饰轴心显示修复。 |
| `EnableVideoBackgroundSyncFix` | true | 启用视频背景同步修复。 |
| `PersistEditorPreferences` | true | 官方编辑器偏好变化后立即保存。 |
| `ShowEditorOverlay` | true | 显示编辑器内快捷浮窗。 |

### 压缩包设置

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `LegacyZipEncoding` | `Auto` | 旧 ZIP 文件名编码。可选 `Auto`、`CP949`、`GB18030`、`ShiftJIS`、`CP437`，只影响下一次压缩包操作。 |

### 浮窗状态

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `EditorOverlayCollapsed` | false | 浮窗是否折叠。 |
| `EditorOverlayX` | -1 | 浮窗 X 坐标，负数表示使用默认位置。 |
| `EditorOverlayY` | -1 | 浮窗 Y 坐标，负数表示使用默认位置。 |

### 编辑器数值

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `DecorationMoveSnapStep` | 0.5 | 装饰移动吸附步进。0 表示关闭。 |
| `FloatStepPerPixel` | 0.1 | 小数字段右键拖动每像素变化量。 |
| `IntStepPerPixel` | 1 | 整数字段右键拖动每像素变化量。 |
| `MaxFloatingPoints` | 3 | 小数字段拖动后保留的小数位数。 |

### 渲染设置

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `ChartRenderWorkspaceDirectory` | Mod 目录下 `Workspace` | 临时文件目录。 |
| `ChartRenderExportDirectory` | 用户视频目录下 `ADOFAI Renders` | 最终 MP4 输出目录。 |
| `ChartRenderWidth` | 1920 | 视频宽度。 |
| `ChartRenderHeight` | 1080 | 视频高度。 |
| `ChartRenderFps` | 60 | 成品帧率。 |
| `ChartRenderCrf` | 18 | 画质参数。NVENC VBR 时作为 CQ；x264 码率模式主要由码率控制。 |
| `ChartRenderBitrateMbps` | 0 | 视频目标码率。0 表示按分辨率和帧率自动推荐。 |
| `ChartRenderPreset` | veryfast | 自定义编码字符串，仅在 `ChartRenderEncoderMode = custom` 时显示。 |
| `ChartRenderEncoderMode` | auto-balanced | 编码档位。默认优先 GPU，并在失败时回退 CPU。 |
| `ChartRenderCaptureFormat` | rgba | GPU readback 格式。`bgra` 是实验模式。 |
| `ChartRenderCaptureSource` | camera | 画面来源。`camera` 为摄像机渲染，`game-view` 为游戏最终画面。 |
| `ChartRenderPreviewMode` | full | 渲染时预览模式。可选完整、暗色、极简。 |
| `ChartRenderAudioFormat` | aac | 音频格式。可选 AAC（有损）、FLAC（无损）、ALAC（无损）。 |
| `ChartRenderVideoFormat` | mp4 | 最终容器。可选 MP4、MKV、MOV。 |
| `ChartRenderCompletionTailSeconds` | 5 | 谱面结束后额外录制秒数。 |
| `ChartRenderAudioSyncOffsetMs` | 0 | 高级兜底音频同步偏移。正数让音频提前，负数让音频延后。 |
| `ChartRenderShowHitJudgments` | true | 导出时是否显示判定文字。 |
| `ChartRenderUseSelectedRange` | false | 是否只渲染编辑器当前框选的连续砖块段落。 |
| `ChartRenderAdvancedSettingsExpanded` | false | UMM 高级渲染设置是否展开。 |
| `ChartRenderProfessionalSettingsExpanded` | false | 专业设置是否展开。 |
| `ChartRenderCustomMuxArgs` | 空 | 自定义合成参数；普通用户保持为空。 |
| `HasShownReadme` | false | 是否已经自动打开过本地用户手册。 |

## UMM 设置 UI

`Settings.OnGUI()` 使用 IMGUI 绘制设置面板。

设计原则：

- 面向玩家的基础设置默认显示。
- CRF、码率、编码档位、workspace、回读格式、预览模式这类不直观的设置放到高级设置里。
- 每个渲染设置都有单独重置按钮。
- 有一键恢复渲染默认。
- 修改渲染设置后立即保存，下一次渲染生效。
- “功能兼容状态”从 `PatchManager.Statuses` 读取，不覆盖用户保存的功能开关。

压缩包设置：

- 旧版 ZIP 文件名编码，默认自动检测。
- 修改后不重新应用补丁，从下一次导入或解压开始生效。

功能兼容状态：

- 显示总体可用数量。
- 每组显示可用、不可用、被依赖项阻止或未启用。
- 失败时 UI 只显示简短原因和失败补丁名，完整异常写入 UMM 日志。

基础渲染设置：

- 导出目录。
- 画面捕获方式：摄像机渲染或游戏画面渲染。
- 分辨率快捷预设：1080p、2K、4K。
- 宽度。
- 高度。
- 帧率快捷预设：30、60、120。
- 帧率。
- 结束后延迟停止秒数。
- 是否显示判定文字。
- 是否仅渲染选中段落。开启后需要在编辑器中框选至少两个连续砖块。

游戏画面模式下，宽高输入和分辨率预设不决定输出尺寸，UI 改为显示当前 `Screen.width × Screen.height`。帧率、视频格式、音频格式、判定文字和片段范围仍然有效。

高级渲染设置：

- 工作区目录。
- 画质参数。
- 视频码率，0 表示自动推荐。常见 60fps 建议值：1080p 20 Mbps、2K 35 Mbps、4K 60 Mbps。
- 编码档位。
- 自定义编码字符串，仅在自定义档位下显示。
- GPU readback 格式。
- 音频格式（AAC / FLAC / ALAC）。
- 视频格式（MP4 / MKV / MOV）。
- 渲染预览模式。
- 音频同步偏移。

专业设置：

- 默认折叠并显示风险提示。
- 自定义合成参数为空时使用内置参数。

## 输入框临时状态

设置类里保存了多个 `xxxText` 字段，例如：

- `renderWidthText`
- `renderHeightText`
- `renderFpsText`
- `renderCrfText`
- `renderPresetText`
- `renderTailSecondsText`

原因是 IMGUI 每帧都会重绘。如果直接把数值格式化回输入框，用户清空或输入半截数字时会被打断。当前实现只在 parse 成功时更新真实设置，输入框文本本身允许临时无效。

## Normalize

`NormalizeChartRenderSettings()` 负责保存前校验：

- 宽度范围：16 到 7680。
- 高度范围：16 到 4320。
- 宽高强制偶数，避免 yuv420p / 编码器失败。
- FPS 范围：1 到 240。
- CRF 范围：0 到 51。
- 码率范围：0 到 300 Mbps。0 表示自动推荐。
- preset 为空则回到 `veryfast`。
- 编码档位非法则回到 `auto-balanced`。
- 回读格式非法则回到 `rgba`。
- 画面捕获方式非法则回到 `camera`。
- 预览模式非法则回到 `full`。
- 音频格式非法则回到 `aac`。
- 视频格式非法则回到 `mp4`。
- 旧 ZIP 文件名编码非法则回到 `Auto`。
- 结束尾巴秒数最小为 0。
- 音频同步偏移范围：-5000 到 5000 毫秒。

## 默认路径

工作区：

```text
<ModPath>/Workspace
```

导出目录：

```text
<MyVideos>/ADOFAI Renders
```

如果系统视频目录为空，则回退到：

```text
<Workspace>/Exports
```

## Localization

`Localization.Load(modEntry)` 从：

```text
<ModPath>/Resources/localization.json
```

读取 JSON。结构：

```json
{
  "entries": [
    {
      "key": "title",
      "en": "ADOFAI Editor Tweaks",
      "zh": "ADOFAI 编辑器优化"
    }
  ]
}
```

语言选择：

- `SystemLanguage.Chinese`
- `SystemLanguage.ChineseSimplified`
- `SystemLanguage.ChineseTraditional`

以上使用 `zh`，其他语言使用 `en`。

缺失处理：

- key 不存在：返回 key 本身。
- 当前语言文本为空：回退到英文。
- 英文也为空：返回 key。

## 新增设置流程

1. 在 `Settings` 添加字段和默认值。
2. 在 `OnGUI()` 中画 UI。
3. 如果是渲染设置，加入 old/new 比较和保存逻辑。
4. 必要时加入 Normalize。
5. 在 `Resources/localization.json` 添加中英文文本。
6. 如果浮窗也需要展示，同步 `EditorTweaksOverlayWindow`。
7. 如果加入 Steam 云同步，更新 `CloudSettingsManager` 的序列化和反序列化映射。
8. 更新 `Resources/README.html`、README 和对应 Doc。

## 踩坑记录

- 宽高必须保持偶数，FFmpeg `yuv420p` 和硬件编码器都更稳。
- 高级设置默认隐藏，否则普通用户会被 CRF / 编码档位 / 回读格式吓到。
- `ChartRenderPreset` 现在只作为 Custom 档位的兼容兜底；普通用户应该使用 `ChartRenderEncoderMode`。
- BGRA readback 只是实验项，默认保持 RGBA 更稳。
- 音频同步偏移只应该作为兜底校准使用。比如音频慢 10 帧且导出 60fps，可先试 `167ms`。
- 摄像机模式使用保存的宽高；游戏画面模式在会话开始时读取并固定当前游戏分辨率。
- 游戏画面模式不使用渲染预览模式，浮窗切换画面来源后要立即保存设置。
- `LegacyZipEncoding` 只控制无可靠 Unicode 名称的旧 ZIP，不应影响其他压缩格式。
- 本地化文件缺失时不能让 Mod 加载失败，只写日志并回退 key。
