# 编辑器 Mod 补丁清单

| 功能组 | 主要目标 | 作用 |
| --- | --- | --- |
| NumericDrag | `PropertyControl_Text/Vector2.Setup`、`DraggableNumberInputField` | 给 Int、Float、Tile、Vector2 输入增加拖动调节。 |
| DecorationSelection | `scnEditor.DragDecorations`、`DecorationPivot`、`scrParallax` | 修复 Camera 相对装饰拖动、轴心和移动吸附。 |
| VideoBackgroundSync | `scrVfxPlus.Reset/Update` | 普通游戏运行时同步视频背景时间。 |
| EditorPreferences | `EditorPreferencesEntry.NotifyChange` | 偏好变化后立即保存。 |
| LevelLoading | `scnEditor.UpdateImageLoadResult` | 合并重复缺图错误记录。 |

每组使用独立 Harmony ID。某一组失败时只回滚该组，不会加载渲染器或压缩包服务。
