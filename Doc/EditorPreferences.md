# EditorPreferences 模块

`Src/EditorPreferences` 只做一件事：官方编辑器偏好设置变化后立即保存。

## 背景

ADOFAI 编辑器里有一些官方偏好设置会先写入内存，实际落盘依赖游戏自己的保存时机。如果游戏异常退出、切场景流程没触发保存，用户刚改的偏好可能丢失。

这个 Mod 不改变偏好项本身，只在官方通知变化后补一次保存。

## Patch

目标：

```text
EditorPreferencesEntry.NotifyChange(EditorPreferencesControl control)
```

类型：

```text
Postfix
```

逻辑：

```text
if (!Main.Settings.PersistEditorPreferences) return
Persistence.generalPrefs.Save()
```

异常处理：

- 捕获所有异常。
- 写入 UMM 日志。
- 不抛出，不影响官方偏好 UI。

## 设置

对应设置：

```text
Settings.PersistEditorPreferences
```

UMM 文本：

```text
persistEditorPreferences
```

默认值：

```text
true
```

## 踩坑记录

- 只保存 `Persistence.generalPrefs`，不碰谱面文件。
- Postfix 比 Prefix 安全，因为要等官方先完成内存状态更新。
- 保存失败不能阻塞官方 UI，否则偏好面板会变得脆弱。
