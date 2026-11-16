# NumericDrag 模块

`src/NumericDrag` 给官方编辑器数值输入框增加右键拖动调节。它复用游戏已有的 `DraggableNumberInputField`，并在拖动时实时写回当前事件。

## 文件职责

| 文件 | 职责 |
| --- | --- |
| `NumericDragFeature.cs` | 判断字段类型、挂载拖动组件、实时应用值、刷新编辑器 UI。 |
| `NumericDragPatches.cs` | Patch 官方控件 setup 和拖动组件鼠标事件。 |
| `EditorTweaksNumericDragMarker.cs` | 标记本 Mod 创建的拖动字段，并保存提交/实时应用回调。 |

## 支持字段

`PropertyControl_Text`：

- `PropertyType.Int`
- `PropertyType.Float`
- `PropertyType.Tile`

`PropertyControl_Vector2`：

- X 输入框
- Y 输入框

## 交互方式

只使用右键拖动。`EditorTweaksNumericDragMarker.IsDragButton()` 返回：

```text
eventData.button == PointerEventData.InputButton.Right
```

这样不会破坏左键点击输入框、选择文字、手动输入等官方行为。

## 挂载流程

Patch：

- `PropertyControl_Text.Setup` Postfix
- `PropertyControl_Vector2.Setup` Postfix

Postfix 调用：

```text
NumericDragFeature.Attach(control)
```

`Attach` 会检查：

- 设置 `EnableNumericDrag`。
- 控件和输入框不为空。
- `propertyInfo` 不为空。
- 输入框上没有已有 `EditorTweaksNumericDragMarker`。

然后添加：

- `EditorTweaksNumericDragMarker`
- `DraggableNumberInputField`

`DraggableNumberInputField` 设置：

- `field = TMP_InputField`
- `arrows = new GameObject[0]`
- `onDrag = new UnityEvent()`
- `axis = Horizontal`
- `clamp = !propertyInfo.ignoreRange`
- `stepPerPixel` 来自设置。
- `maxFloatingPoints` 来自设置。
- min/max 来自 `PropertyInfo`。

Tile 字段的最大值特殊处理：如果能拿到 `scnGame.instance.levelMaker.listFloors`，最大值是最后一块地板。

## 实时应用

### Text 字段

`LiveApply(PropertyControl_Text)`：

1. 调用 `control.ValidateInput()`。
2. 取得当前 selected event。
3. 按 `PropertyType` 解析输入框文本。
4. 如果属性名是 `floor`，写入 `selectedEvent.floor`。
5. 否则写入 `selectedEvent[propertyName]`。
6. `control.ToggleOthersEnabled()`。
7. 如果关联 slider，更新 slider 值。
8. 刷新：
   - BackgroundSettings：`ADOBase.customLevel.SetBackground()`
   - Decoration：`ADOBase.editor.UpdateDecorationObject(selectedEvent)`
   - Tile changes：`control.ApplyTileChanges()`
   - 单选 floor 指示器：`ShowEventIndicators`
   - `control.OnValueChange()`

异常会被吞掉，因为官方输入框在编辑中可能临时出现空字符串或非法文本。

### Vector2 字段

`LiveApply(PropertyControl_Vector2)`：

1. `ValidateInput()`。
2. 解析 X/Y。
3. 空字符串按 `NaN` 处理，保持官方编辑体验。
4. 写入 `selectedEvent[propertyInfo.name] = new Vector2(x, y)`。
5. 刷新装饰、背景或轨道类事件。

轨道类事件包括：

- `PositionTrack`
- `FreeRoam`
- `FreeRoamTwirl`
- `FreeRoamRemove`
- `FreeRoamWarning`

这些事件变化后需要 `ADOBase.editor.ApplyEventsToFloors()`，并更新 floor button canvas 位置。

## 拖动组件 Patch

### `DraggableNumberInputField.OnPointerDown`

只接管带 marker 的组件。

如果不是右键，直接 `return false`，避免官方拖动逻辑误触发。

如果文本不能 parse 成 float，也返回 false。

成功时：

- `field.DeactivateInputField()`
- 写私有字段：
  - `_startValue`
  - `_startPos`
  - `_isDragging = false`
  - `_down = true`
- `return false`

为什么要写私有字段：官方 `DraggableNumberInputField` 内部拖动状态没有公开 API，只能复用它的 Update/drag 逻辑。

### `DraggableNumberInputField.OnPointerUp`

只接管带 marker 的组件。

如果拖动过：

```text
marker.CommitAfterDrag()
```

它会调用传入的 `Commit`，通常是输入框 `onEndEdit.Invoke(text)`。

### `DraggableNumberInputField.SetArrowsVisible`

本 Mod 不创建左右箭头 UI，所以 `arrows` 可能为空。Prefix：

```text
return __instance.arrows != null
```

避免官方方法访问空引用。

## 踩坑记录

- 不要用左键拖动，会破坏输入框正常编辑。
- 拖动中必须实时写回事件，否则装饰和背景不会跟着动。
- 只改输入框文本不够，官方 inspector 的事件数据不会自动更新。
- Tile 字段不是单纯 int，事件属性里可能是 `Tuple<int, TileRelativeTo>`。
- Vector2 空字符串要按 `NaN` 处理，不能强行归零。
- 使用官方私有字段时要关注游戏更新后字段名是否变化。
