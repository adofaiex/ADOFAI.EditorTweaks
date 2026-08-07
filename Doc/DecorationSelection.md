# DecorationSelection 模块

`src/Features/DecorationSelection` 修复装饰编辑里的三个问题：

- Camera / CameraAspect 相对装饰拖动不符合屏幕空间直觉。
- Camera / 视差装饰的轴心十字位置不准。
- 拖动装饰时缺少稳定的坐标吸附。

## 文件职责

| 文件 | 职责 |
| --- | --- |
| `CameraRelativeDecorationDragPatches.cs` | 接管包含 Camera / CameraAspect 装饰的拖动计算。 |
| `DecorationMoveSnapPatches.cs` | 拖动后按设置步进吸附装饰坐标。 |
| `DecorationPivotPatches.cs` | 修正装饰轴心十字和屏幕相对坐标。 |

## Camera 相对装饰拖动

官方拖动逻辑主要按世界坐标处理。Camera / CameraAspect 装饰的数据坐标本质上更接近屏幕空间：

- `Camera` 受相机宽高比影响。
- `CameraAspect` 使用另一套 aspect 处理。

如果直接套普通装饰拖动，用户拖动鼠标时装饰移动比例会不自然，尤其缩放、宽屏或官谱镜头下很明显。

### DragDecorationsStart

Patch：

```text
scnEditor.DragDecorationsStart Postfix
```

访问私有字段：

```text
decorationPositionsAtDragStart
```

对 Camera / CameraAspect 装饰，把缓存起点改成事件里的数据坐标：

```text
positions[decoration] = (Vector2)selectedDecoration["position"]
```

这样后续拖动不会从装饰 transform 的世界坐标出发。

### DragDecorations

Patch：

```text
scnEditor.DragDecorations Prefix
```

只有在选中项包含 Camera / CameraAspect 装饰时接管，否则返回 true 走官方逻辑。

接管时会：

1. 读取拖动开始缓存。
2. 根据 translation 判断当前轴锁定偏好：
   - 私有字段 `addXDragCache`
   - 私有字段 `addYDragCache`
3. 遍历选中 decoration。
4. 跳过 locked 或 forceLock。
5. Camera / CameraAspect 走 `DragCameraRelativeDecoration()`。
6. 其他装饰走 `DragRegularDecoration()`，保持混选时也能正常动。
7. 单选时刷新 inspector 的 `position` 文本。
8. 返回 false，阻止官方方法再执行一遍。

### Camera / CameraAspect 换算

```text
screenUnitsPerWorldUnit = 20 / (camera.orthographicSize * 2)
delta = translation * screenUnitsPerWorldUnit
if relativeTo == Camera:
    delta.x /= camera.aspect
```

最终：

```text
newPosition = ApplyAxisLock(startPosition, startPosition + delta)
selectedDecoration["position"] = newPosition
decoration.SetPosition(newPosition, decoration.pivotOffsetVec)
```

### 普通装饰混选

普通装饰仍使用官方 `editor.GetDecorationDragDelta(translation, decoration)`，但修复逻辑会处理：

- parallax 为 100 时对应轴不移动。
- Tile 相对装饰要减去地板世界坐标。
- 数据坐标要除以 `ADOBase.controller.tileSize`。

## 坐标吸附

Patch：

```text
scnEditor.DragDecorations Postfix
```

触发条件：

- `DecorationMoveSnapStep > 0`
- `__instance.draggingGizmo == null`

对每个选中装饰：

1. 读取 `selectedDecoration["position"]`。
2. 按步进 round：
   ```text
   round(value / step) * step
   ```
3. 极小值归零。
4. 写回事件。
5. 调用 `decoration.SetPosition()` 刷新 transform。
6. 单选时刷新 inspector 文本。

不同坐标系：

- Camera / CameraAspect：数据坐标就是 pivot 位置。
- Tile：数据坐标乘 tileSize 后加 floor 世界坐标。
- 其他：数据坐标乘 tileSize。

## 轴心十字修复

### `DecorationPivot.UpdatePivotCrossImage`

Prefix 接管官方方法。

逻辑：

- 未选或多选时隐藏 gizmo。
- 单选时拿到 `scrDecorationManager.GetDecoration(selectedEvent)`。
- `gizmoTransform.position = decoration.pivotPosVec`，与游戏原版保持一致，避免把图片/视差偏移误当成轴心位置。
- 根据 `enable && !hide` 设置 active。

### `scrDecoration.UpdateScreenClamp`

Postfix 修正屏幕相对装饰的 parallax 数据。

增加三层守卫：

1. 只在编辑器环境生效（`ADOBase.editor != null && ADOBase.isEditingLevel`），不干预游戏播放。
2. 只对当前选中的装饰修正位置（`selectedDecorations.Contains(sourceLevelEvent)`），不影响其他装饰。
3. 不覆盖游戏原有的 `clampToScreen` 状态，保留游戏对装饰可见性的控制。

CameraAspect 要先：

```text
pivot.x *= Screen.height / Screen.width
```

其中 `pivot = pivotPosVec + pivotOffsetVec`，必须同时保留装饰轴心位置和图片偏移。

然后：

```text
parallax.screenRelativePos = pivot / 20 + (0.5, 0.5)
```

### `scrParallax.SetTrans`

Postfix 在视差 transform 更新后，如果当前单选装饰就是这个 parallax 所属装饰，则使用已经完成镜头变换的 `decoration.transform.position` 刷新轴心十字。这里不能使用未变换的 `pivotPosVec`，否则十字会在每帧刷新时漂离装饰。

## 踩坑记录

- Camera / CameraAspect 不能按普通世界坐标拖。
- 拖动起点必须是事件数据坐标，不是 transform 坐标。
- 混选时普通装饰仍要处理，不能只移动 Camera 相对装饰。
- Shift 轴锁定依赖官方私有 cache，修复路径里也要维护。
- Tile 相对装饰写回数据坐标前必须减 floor 世界坐标并除 tileSize。
- 吸附应该在拖动后做，避免干扰官方 gizmo 拖动。
