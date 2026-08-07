# Harmony Patch 清单

本文档列出当前项目的全部 Harmony Patch。新增、删除或改动 Patch 时必须同步更新这里。

## PatchManager 隔离策略

Mod 启用时不会再对程序集执行一次性的 `PatchAll`。PatchManager 将全部 34 个 Harmony Patch 划分为 10 个功能组，每组使用独立的 Harmony ID：

| 功能组 | Patch 数量 | 依赖 |
| --- | ---: | --- |
| Numeric Drag | 5 | 无 |
| Camera Relative Decoration Drag | 2 | 无 |
| Decoration Move Snap | 1 | 无 |
| Decoration Pivot | 3 | 无 |
| Video Background Sync | 2 | 无 |
| Editor Preferences | 1 | 无 |
| Editor Overlay Input Guard | 9 | 无 |
| Chart Rendering | 8 | Editor Overlay Input Guard |
| Archive I/O | 2 | 无 |
| Image Load Error Deduplication | 1 | 无 |

同组任一 Patch 应用失败时会卸载该组已经应用的全部 Patch，并继续加载其他功能组。Chart Rendering 的依赖组不可用时不会尝试应用，以免离线渲染在缺少输入保护的情况下进入半可用状态。设置页显示各组兼容状态，完整异常记录在 Unity Mod Manager 日志中。

## NumericDrag

| 文件 | 目标方法 | 类型 | 条件 | 作用 | 风险点 |
| --- | --- | --- | --- | --- | --- |
| `NumericDragPatches.cs` | `PropertyControl_Text.Setup` | Postfix | `EnableNumericDrag` 为 true，字段类型是 Int / Float / Tile | 调用 `NumericDragFeature.Attach(PropertyControl_Text)` 给 TMP 输入框挂拖动组件 | 官方控件结构变化时 `inputField` 或 `propertyInfo` 可能为空 |
| `NumericDragPatches.cs` | `PropertyControl_Vector2.Setup` | Postfix | `EnableNumericDrag` 为 true | 给 Vector2 的 X/Y 输入框分别挂拖动组件 | Vector2 min/max 来自 `propertyInfo.minVec/maxVec` |
| `NumericDragPatches.cs` | `DraggableNumberInputField.OnPointerDown` | Prefix | 组件上存在 `EditorTweaksNumericDragMarker` | 只允许右键拖动，写入 `_startValue`、`_startPos`、`_isDragging`、`_down` | 使用官方私有字段名，官方重命名会失效 |
| `NumericDragPatches.cs` | `DraggableNumberInputField.OnPointerUp` | Prefix | 组件上存在 marker | 停止拖动并在确实拖动过时提交 `onEndEdit` | 要避免普通左键编辑输入框被误拦 |
| `NumericDragPatches.cs` | `DraggableNumberInputField.SetArrowsVisible` | Prefix | 所有实例 | `arrows == null` 时跳过官方方法 | 防御性 Patch，避免本 Mod 创建无箭头组件时报错 |

## DecorationSelection

| 文件 | 目标方法 | 类型 | 条件 | 作用 | 风险点 |
| --- | --- | --- | --- | --- | --- |
| `CameraRelativeDecorationDragPatches.cs` | `scnEditor.DragDecorationsStart` | Postfix | `EnableCameraRelativeDecorationDragFix` | 对 Camera / CameraAspect 装饰，把拖动起点缓存改为事件数据坐标 | 访问私有字段 `decorationPositionsAtDragStart` |
| `CameraRelativeDecorationDragPatches.cs` | `scnEditor.DragDecorations` | Prefix | 选中项包含 Camera / CameraAspect 装饰 | 接管拖动计算，按屏幕空间换算位置，保留 Shift 轴锁定 | 访问私有字段 `addXDragCache`、`addYDragCache` |
| `DecorationMoveSnapPatches.cs` | `scnEditor.DragDecorations` | Postfix | `DecorationMoveSnapStep > 0` 且不是 gizmo 拖动 | 对拖动后的 `position` 做 round 吸附并刷新 UI | 需要分别处理 Tile、Camera、CameraAspect 坐标系 |
| `DecorationPivotPatches.cs` | `DecorationPivot.UpdatePivotCrossImage` | Prefix | `EnableDecorationPivotFix` | 单选装饰时把轴心十字放到实际 decoration transform | 多选或未选时隐藏 |
| `DecorationPivotPatches.cs` | `scrDecoration.UpdateScreenClamp` | Postfix | Camera / CameraAspect 装饰 | 修正 `scrParallax.screenRelativePos`，让屏幕 clamp 坐标正确 | CameraAspect 要乘 `Screen.height / Screen.width` |
| `DecorationPivotPatches.cs` | `scrParallax.SetTrans` | Postfix | 当前选中的装饰就是该 parallax 所属装饰 | 视差变换后刷新轴心十字 | 只在编辑器且单选时执行 |

## VideoBackgroundSync

| 文件 | 目标方法 | 类型 | 条件 | 作用 | 风险点 |
| --- | --- | --- | --- | --- | --- |
| `VideoBackgroundSyncPatches.cs` | `scrVfxPlus.Reset` | Postfix | 总是 | 根据实例 ID 删除同步状态 | 防止复用 VFX 实例时沿用旧状态 |
| `VideoBackgroundSyncPatches.cs` | `scrVfxPlus.Update` | Postfix | `EnableVideoBackgroundSyncFix` 且视频准备完毕、conductor 已启动、非暂停、Full VFX | 校正 `VideoPlayer.time` 和 `playbackSpeed` | seek 不能太频繁，否则长视频会卡；所以有启动窗口和 cooldown |

## EditorPreferences

| 文件 | 目标方法 | 类型 | 条件 | 作用 | 风险点 |
| --- | --- | --- | --- | --- | --- |
| `EditorPreferencesPersistencePatches.cs` | `EditorPreferencesEntry.NotifyChange` | Postfix | `PersistEditorPreferences` | 调用 `Persistence.generalPrefs.Save()` | 保存失败只写日志，不能打断官方 UI |

## RenderInputGuard

| 文件 | 目标方法 | 类型 | 条件 | 作用 | 风险点 |
| --- | --- | --- | --- | --- | --- |
| `RenderInputGuardPatches.cs` | `scnEditor.Update` | Prefix | 渲染任务活跃 | 阻止编辑器响应点击、滚轮、键盘 | 不影响控制器更新 |
| `RenderInputGuardPatches.cs` | `scnEditor.ZoomCamera` | Prefix | 渲染任务活跃 | 防止滚轮穿透导致缩放 | 即使某些路径绕过 Update 直接调用 ZoomCamera，也能拦住 |
| `RenderInputGuardPatches.cs` | `scrController.Update` | Prefix | 始终放行 | 保持控制器与离线视觉时钟更新 | 渲染期间不能拦截 |
| `RenderInputGuardPatches.cs` | `scrController.TogglePauseGame` | Prefix | 渲染任务活跃 | 阻止用户按键暂停游戏 | 返回当前 paused 状态，保持调用方语义 |
| `RenderInputGuardPatches.cs` | `scrPlayerManager.AnyValidInputWasTriggered` | Prefix | 渲染任务活跃 | 阻止 Press To Start、结算退出等玩家输入 | 自动打击不走这个路径 |
| `RenderInputGuardPatches.cs` | `scrPlayer.ValidInputWasTriggered` | Prefix | 渲染任务活跃 | 阻止键盘/鼠标输入触发命中 | 自动打击直接调用 `Hit(isAuto: true)` |
| `RenderInputGuardPatches.cs` | `scrPlayer.ValidInputWasReleased` | Prefix | 渲染任务活跃 | 阻止用户松键影响 hold | 自动打击路径不会依赖用户松键 |
| `RenderInputGuardPatches.cs` | `scrPlayer.CountValidKeysPressed` | Prefix | 渲染任务活跃 | 返回 0，阻止输入计数 | 防止 multipress、hold 等逻辑被人工输入污染 |
| `RenderInputGuardPatches.cs` | `StandaloneInputModule.Process` | Prefix | 渲染任务活跃 | 阻止 Unity UI 背景点击 | Web 页面位于外部浏览器 |

## ChartRendering

| 文件 | 目标方法 | 类型 | 条件 | 作用 | 风险点 |
| --- | --- | --- | --- | --- | --- |
| `ChartRenderVisualClock.cs` | `scrConductor.set_songposition_minusi` | Prefix | `ChartRenderVisualClock.TryGetSongPosition` 成功 | 把 conductor 视觉时间强制为输出帧时间 | 必须在播放 schedule 后锚定，否则起点相位会错 |
| `ChartRenderVisualClock.cs` | `scrConductor.get_songposition_minusi` | Postfix | `ChartRenderVisualClock.TryGetSongPosition` 成功 | 读取视觉时间时返回当前输出帧对应的强制时间 | 与 setter Patch 配合，避免游戏 Update 覆盖离线时间轴 |
| `ChartRenderAutoPlayer.cs` | `scrConductor.Update` | Postfix | `IsRendering`、`IsAutoPlaybackReady` 且视觉时钟活跃 | 自动补打当前帧应命中的砖块 | 必须等视觉时钟锚定后才允许自动打击；每帧最多 16 次 |
| `ChartRenderAutoPlayer.cs` | `AsyncInputUtils.AdjustAngle(scrPlayer, ulong)` | Prefix | `IsRendering` | 跳过异步输入角度修正，记录 suppressed 计数 | 这是防止球突然跳角的重要 Patch |
| `ChartRenderAutoPlayer.cs` | `scrPlayer.Hit` | Prefix | 渲染选中段落且下一砖超过结束砖块 | 阻止自动打击越过选中段落终点 | 只影响离线渲染期间的段落边界 |
| `ChartRenderAudioPatches.cs` | `scrSfx.PlaySfx(AudioClip, MixerGroup, float, float, float)` | Prefix | `IsRendering && group == InterfaceParent` | 屏蔽 UMM / 菜单 / 界面音效进入音频捕获 | 只屏蔽 InterfaceParent，不屏蔽谱面音效 |
| `ChartRenderJudgmentPatches.cs` | `scrHitTextManager.ShowHitText(HitMargin, scrPlanet, float)` | Prefix | `IsRendering && !ChartRenderShowHitJudgments` | 导出时隐藏 Perfect / Early / Late 等判定字 | 只影响渲染期间 |
| `ChartRenderCustomFrameRate.cs` | `scrCamera.UpdateCustomFrameRateScreen` | Prefix | 离线渲染期间 | 记录自定义帧率画面刷新，只在游戏刷新画面时捕获新帧 | 避免自定义 FPS 谱面产生无意义的重复捕获 |

## ArchiveIo

| 文件 | 目标方法 | 类型 | 条件 | 作用 | 风险点 |
| --- | --- | --- | --- | --- | --- |
| `ArchiveIoPatches.cs` | `ZipUtils.Unzip` | Prefix | ArchiveIo 组初始化成功 | 按文件内容识别 ZIP/ADOZIP、RAR、7z、TAR、GZip、BZip2、XZ、CAB 等格式并逐条目安全解压；旧 ZIP 额外修复文件名编码 | `7z.dll` 缺失或位数错误时整组回滚，继续使用原版 |
| `ArchiveIoPatches.cs` | `ZipUtils.Zip` | Prefix | ArchiveIo 组初始化成功 | 使用标准 ZIP Deflate 导出并保留资源相对目录 | 导出文件存在重复相对路径时会明确失败 |

ArchiveIo 启用时会把常见压缩格式加入编辑器打开谱面和 CLS 导入的文件筛选列表，停用 Mod 时恢复游戏原有列表。压缩导出仍固定生成兼容原版游戏的 ZIP 格式 `.adozip`。

## LevelLoading

| 文件 | 目标方法 | 类型 | 条件 | 作用 | 风险点 |
| --- | --- | --- | --- | --- | --- |
| `ImageLoadErrorDeduplicationPatches.cs` | `scnEditor.UpdateImageLoadResult` | Prefix | 加载谱面时，同名缺图已经记录过 | 更新已有记录并跳过原版重复 `Add`，避免谱面加载协程中断 | 访问私有字段 `errorImageResult`、`isUnauthorizedAccess`；游戏改名时该组会回滚 |

## 非 Harmony 但同样关键的 Hook

| 文件 | API / 类型 | 作用 | 风险点 |
| --- | --- | --- | --- |
| `ChartFrameCapture.cs` | `AsyncGPUReadback.Request` | 从专用 RenderTexture 异步读回 RGBA/BGRA 帧 | GPU readback 失败会抛异常并终止渲染 |
| `ChartRenderFramePipeline.cs` | pending queue + buffer pool | 限制 GPU readback pending，复用帧 buffer，向 FFmpeg 写入并支持反压 | 队列满会降低处理速度，但不能改变输出时间轴 |
| `ChartRenderMemoryBudget.cs` | 分辨率预算 | 按 `width * height * 4` 计算缓存上限、pending 上限和 FFmpeg 队列上限 | 4K/8K 必须优先防止内存峰值失控 |
| `ChartUnityAudioCapture.cs` | `AudioRenderer.Start/Render/Stop` | 离线捕获 Unity mixer 输出；连续没有样本时按帧率等待并自动恢复一次 | 只能写入 Unity 返回的原始样本，不能按视频帧强制补齐或截断音频 |
| `FfmpegEncoder.cs` | `Process` + stdin pipe | rawvideo 进入 FFmpeg 编码 | writer 线程异常需要回传主流程 |
| `ADOFAIMod.targets` | MSBuild Target | 下载 FFmpeg、复制资源、部署 Mod、生成 Build 产物和 zip | 不要提交 ffmpeg.exe 或 Build 产物到 git |
