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
| `WebUiOpenHotkey` | `Ctrl+Shift+E` | 打开本地 Web 设置页的快捷键。 |

`ShowEditorOverlay` 仍保留为旧配置兼容字段，但不再参与 UI 和运行逻辑。

### 压缩包设置

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `LegacyZipEncoding` | `Auto` | 旧 ZIP 文件名编码。可选 `Auto`、`CP949`、`GB18030`、`ShiftJIS`、`CP437`，只影响下一次压缩包操作。 |

### 旧浮窗兼容字段

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `EditorOverlayCollapsed` | false | 旧浮窗折叠状态，只读取不再使用。 |
| `EditorOverlayX` | -1 | 旧浮窗 X 坐标，只读取不再使用。 |
| `EditorOverlayY` | -1 | 旧浮窗 Y 坐标，只读取不再使用。 |

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
| `ChartRenderCustomMuxArgs` | 空 | 自定义合成参数；普通用户保持为空。 |

### 旧配置兼容字段

以下字段仍保留在配置对象中，以便旧设置文件和云配置可以继续读取，但不再参与界面或运行逻辑：

| 字段 | 说明 |
| --- | --- |
| `ChartRenderAdvancedSettingsExpanded` | 旧版渲染设置展开状态。 |
| `ChartRenderProfessionalSettingsExpanded` | 旧版专业设置展开状态。 |
| `HasShownReadme` | 旧版首次自动打开手册的记录。当前版本不会自动打开手册。 |

## UMM 设置 UI

`Settings.OnGUI()` 只使用 IMGUI 绘制 Web 页面快捷键录入行。完整设置位于外部 Web 页面。

设计原则：

- UMM 只负责快捷键录入和保存。
- 基础设置、渲染设置和兼容状态由 Web 页面展示。
- Web 页面修改后立即进入 Unity 主线程队列并保存。
- 渲染进度和取消按钮位于同一个 Web 页面。

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

## Web 页面输入与保存

Web 页面中的文本和数字输入框保留本地编辑中的临时内容，在失焦或按下 Enter 后提交。提交成功会显示保存提示；服务端会再次校验范围、格式和默认值。渲染进行时，所有会影响任务输出的输入框、下拉菜单、开关和恢复默认操作都会锁定，避免修改已开始任务使用的配置。

## Normalize

`Normalize()` 负责保存前校验：

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
2. 在 `WebUiStateBuilder` 和前端类型中加入字段。
3. 在 `WebUiHost.ApplySettings` 中加入白名单转换和校验。
4. 必要时加入 `Settings.Normalize()`。
5. 在 `Resources/localization.json` 添加中英文文本。
6. 如果加入 Steam 云同步，更新 `CloudSettingsManager` 的序列化和反序列化映射。
7. 更新 `Resources/README.html`、README 和对应 Doc。

## 踩坑记录

- 宽高必须保持偶数，FFmpeg `yuv420p` 和硬件编码器都更稳。
- 高级设置默认隐藏，否则普通用户会被 CRF / 编码档位 / 回读格式吓到。
- `ChartRenderPreset` 现在只作为 Custom 档位的兼容兜底；普通用户应该使用 `ChartRenderEncoderMode`。
- BGRA readback 只是实验项，默认保持 RGBA 更稳。
- 音频同步偏移只应该作为兜底校准使用。比如音频慢 10 帧且导出 60fps，可先试 `167ms`。
- 摄像机模式使用保存的宽高；游戏画面模式在会话开始时读取并固定当前游戏分辨率。
- 游戏画面模式不使用渲染预览模式，Web 页面切换画面来源后要立即保存设置。
- `LegacyZipEncoding` 只控制无可靠 Unicode 名称的旧 ZIP，不应影响其他压缩格式。
- 本地化文件缺失时不能让 Mod 加载失败，只写日志并回退 key。
