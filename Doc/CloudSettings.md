# CloudSettings

Steam Cloud 设置同步功能的技术文档。面向其他 ADOFAI Mod 开发者，讲解如何在自己的 Mod 里接入 Steam Cloud。

---

## 功能概览

利用 `SteamRemoteStorage` API 实现 Mod 设置的跨设备同步。云端数据以 JSON 格式存储，与 ADOFAI 的游戏存档 (`data.sav`) 完全隔离，互不干扰。

本 Mod 当前采用**手动同步**模式：用户在设置面板主动点击「上传」或「下载」按钮来触发云端读写，不自动同步。

---

## 前置条件

### 1. 引用 Steamworks.NET

游戏自带 `com.rlabrecque.steamworks.net.dll`，位于 `Managed` 目录。在 `.csproj` 中引用即可：

```xml
<Reference Include="com.rlabrecque.steamworks.net">
  <HintPath>$(ManagedAssembliesDir)\com.rlabrecque.steamworks.net.dll</HintPath>
</Reference>
```

`$(ManagedAssembliesDir)` 在 `ADOFAIMod.targets` 中定义，如下即可直接使用。

### 2. 检查 Steam 可用性

所有 Steam Cloud 操作前都必须检查 `SteamManager.Initialized`。**Steam 未初始化时（离线模式、非 Steam 版游戏、直接双击 exe 启动），API 调用会失败甚至抛异常。**

```csharp
using Steamworks;

if (!SteamManager.Initialized)
{
    // Steam 不可用，跳过云操作
    return;
}
```

---

## 架构总览

```
┌──────────────────────────────────────────────────┐
│                    Settings.cs                    │
│  ┌──────────────┐  ┌───────────────────────────┐ │
│  │ DrawCloudSync │  │  ToCloudDict()            │ │
│  │ Section()    │  │  FromCloudDict()           │ │
│  │ (UI 按钮)     │  │  (序列化 / 反序列化)       │ │
│  └──────┬───────┘  └───────────┬───────────────┘ │
│         │ 点击按钮               │ 字段映射         │
└─────────┼───────────────────────┼─────────────────┘
          │                       │
          ▼                       ▼
┌──────────────────────────────────────────────────┐
│              CloudSettingsManager.cs              │
│  ┌─────────────────────────────────────────────┐ │
│  │  TryReadFromCloud(Settings)                 │ │
│  │    → SteamRemoteStorage.FileRead()          │ │
│  │    → Json.Deserialize()                     │ │
│  │    → FromCloudDict() 写回 Settings           │ │
│  ├─────────────────────────────────────────────┤ │
│  │  WriteToCloud(Settings)                     │ │
│  │    → ToCloudDict() 提取字段                  │ │
│  │    → Json.Serialize()  + 版本号              │ │
│  │    → SteamRemoteStorage.FileWrite()         │ │
│  ├─────────────────────────────────────────────┤ │
│  │  IsSteamAvailable / HasCloudFile()          │ │
│  │    → SteamManager.Initialized               │ │
│  │    → SteamRemoteStorage.FileExists()        │ │
│  └─────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────┘
          │
          ▼
┌──────────────────────────────────────────────────┐
│              Steam Cloud                          │
│  文件名: ado_fai_editor_tweaks_settings           │
│  内容: { cloud_version, settings: {...} }         │
└──────────────────────────────────────────────────┘
```

---

## 接入步骤

下面以**最简单的 Mod** 为例，演示如何从零接入 Steam Cloud。假设你的 Mod 有两个设置项：`Volume`（float）和 `ShowFps`（bool）。

### 步骤 1：创建云同步管理器

在你的 Mod 项目中新建 `CloudSync.cs`（名字随意），放入以下骨架：

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using GDMiniJSON;
using Steamworks;

namespace MyMod.Features.CloudSync
{
    public static class CloudSync
    {
        // 云端文件名 —— 建议用 Mod ID + "_settings" 避免和其他 Mod 冲突
        private const string CloudFileName = "my_mod_settings";

        // ========== 公开 API ==========

        public static bool IsSteamAvailable => SteamManager.Initialized;

        public static bool HasCloudFile()
        {
            return SteamManager.Initialized
                && SteamRemoteStorage.FileExists(CloudFileName);
        }

        /// <summary>从云端读取设置，覆盖传入的 Settings 对象。返回是否读取成功。</summary>
        public static bool TryReadFromCloud(MySettings settings)
        {
            if (!SteamManager.Initialized)
                return false;

            if (!SteamRemoteStorage.FileExists(CloudFileName))
                return false;

            int fileSize = SteamRemoteStorage.GetFileSize(CloudFileName);
            if (fileSize <= 0)
                return false;

            byte[] data = new byte[fileSize];
            int bytesRead = SteamRemoteStorage.FileRead(CloudFileName, data, fileSize);
            if (bytesRead <= 0)
                return false;

            string json = Encoding.UTF8.GetString(data);
            var root = Json.Deserialize(json) as Dictionary<string, object>;
            if (root == null)
                return false;

            if (!root.TryGetValue("settings", out object settingsObj)
                || !(settingsObj is Dictionary<string, object> dict))
                return false;

            // 把 dict 的值写入 settings 对象
            settings.Volume = GetFloat(dict, "volume", 1f);
            settings.ShowFps = GetBool(dict, "showFps", false);

            return true;
        }

        /// <summary>将 Settings 对象序列化并上传到云端。返回是否写入成功。</summary>
        public static bool WriteToCloud(MySettings settings)
        {
            if (!SteamManager.Initialized)
                return false;

            var root = new Dictionary<string, object>
            {
                ["version"] = "1.0.0",   // 建议带上版本号，方便将来格式迁移
                ["settings"] = new Dictionary<string, object>
                {
                    ["volume"] = settings.Volume,
                    ["showFps"] = settings.ShowFps
                }
            };

            string json = Json.Serialize(root);
            byte[] data = Encoding.UTF8.GetBytes(json);
            return SteamRemoteStorage.FileWrite(CloudFileName, data, data.Length);
        }

        // ========== 类型安全的值提取（JSON 反序列化后 object 类型不确定） ==========

        private static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
        {
            return d.TryGetValue(key, out object v) && v is bool b ? b : fallback;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float fallback)
        {
            if (!d.TryGetValue(key, out object v))
                return fallback;
            if (v is float f) return f;
            if (v is double dbl) return (float)dbl;
            if (v is long l) return l;
            if (v is int i) return i;
            return fallback;
        }
    }
}
```

**几个关键点：**

- `Json.Serialize` / `Json.Deserialize` 来自 `GDMiniJSON`，游戏和 UMM 都内置了，不需要额外依赖。
- `GetFloat` 要处理多种数值类型，因为 `GDMiniJSON` 反序列化时可能把数字变成 `int`、`long`、`double` 或 `float`。
- `cloud_version` 版本号**不是必须的**，但强烈建议加上——将来设置结构变了可以靠版本号做迁移，而不是直接报错。

### 步骤 2：定义序列化映射

在实际项目中，设置字段会比示例多很多。建议把序列化和反序列化集中到 Settings 类自身：

```csharp
public class MySettings : UnityModManager.ModSettings
{
    public float Volume = 1f;
    public bool ShowFps = false;

    // 导出为云端字典
    public Dictionary<string, object> ToCloudDict()
    {
        return new Dictionary<string, object>
        {
            ["volume"] = Volume,
            ["showFps"] = ShowFps
        };
    }

    // 从云端字典恢复（带 fallback 值）
    public void FromCloudDict(Dictionary<string, object> d)
    {
        Volume = GetFloat(d, "volume", 1f);
        ShowFps = GetBool(d, "showFps", false);
    }
}
```

这样 `CloudSync` 内部就不需要知道 Settings 有哪些字段，调用 `settings.ToCloudDict()` / `settings.FromCloudDict(dict)` 即可。本 Mod 正是这样做的。

### 步骤 3：接入 Mod 生命周期

#### 3a. Mod 加载时读取云端

在 `Main.Load()` 中，先读本地 Settings.xml，再尝试用云端数据覆盖：

```csharp
public static bool Load(UnityModManager.ModEntry modEntry)
{
    MySettings settings = MySettings.Load(modEntry);

    // 可选：加载时自动从云端读取
    // CloudSync.TryReadFromCloud(settings);

    modEntry.OnGUI = settings.OnGUI;
    modEntry.OnSaveGUI = settings.OnSaveGUI;
    return true;
}
```

**本 Mod 不自动读取云端**——改为用户在面板上手动点按钮触发。自动 vs 手动的取舍：

| 方式 | 优点 | 缺点 |
| --- | --- | --- |
| 自动同步 | 无感，不用操心 | 多设备间可能互相覆盖；用户不知道当前是谁的配置 |
| 手动同步 | 用户完全可控，知道自己在干什么 | 需要做 UI |

### 步骤 4：添加手动同步 UI（推荐）

在 Settings 的 `OnGUI` 中加两个按钮，并给出操作反馈：

```csharp
private string syncMessage = string.Empty;
private bool syncIsError;

private void DrawCloudSyncSection()
{
    if (!CloudSync.IsSteamAvailable)
    {
        GUILayout.Label("Steam 不可用，云同步功能已禁用。");
        return;
    }

    GUILayout.BeginHorizontal();

    if (GUILayout.Button("下载设置存档", GUILayout.Height(34), GUILayout.Width(180)))
    {
        if (!CloudSync.HasCloudFile())
        {
            syncMessage = "云端没有存档。";
            syncIsError = true;
        }
        else if (CloudSync.TryReadFromCloud(this))
        {
            Save(modEntry);  // 把云端值持久化到本地 Settings.xml
            syncMessage = "已从云端下载设置。";
            syncIsError = false;
        }
        else
        {
            syncMessage = "下载失败，请查看日志。";
            syncIsError = true;
        }
    }

    if (GUILayout.Button("上传设置存档", GUILayout.Height(34), GUILayout.Width(180)))
    {
        NormalizeSettings();
        Save(this, modEntry);  // 先写本地

        if (CloudSync.WriteToCloud(this))
        {
            syncMessage = "设置已上传至云端。";
            syncIsError = false;
        }
        else
        {
            syncMessage = "上传失败，请查看日志。";
            syncIsError = true;
        }
    }

    GUILayout.EndHorizontal();

    if (!string.IsNullOrEmpty(syncMessage))
    {
        Color prev = GUI.contentColor;
        GUI.contentColor = syncIsError ? Color.red : Color.green;
        GUILayout.Label(syncMessage);
        GUI.contentColor = prev;
    }
}
```

这段代码展示的是**完整的手动同步 UI 模式**。核心逻辑是：

1. 检查 Steam 可用性 → 不可用就显示提示，按钮不出现
2. 下载：`TryReadFromCloud()` → 成功则 `Save()` 持久化 → 显示结果
3. 上传：先 `Save()` 持久化本地 → `WriteToCloud()` 推送到云端 → 显示结果
4. 操作结果用绿色/红色文本即时反馈

---

## 数据格式与版本管理

### 云端 JSON 结构

```json
{
  "cloud_version": "1.3.3",
  "settings": {
    "EnableNumericDrag": true,
    "ChartRenderWidth": 1920,
    "ChartRenderExportDirectory": "C:\\Users\\...\\Videos\\ADOFAI Renders"
  }
}
```

- `cloud_version` — Mod 版本号字符串，用于将来做格式兼容判断
- `settings` — 一个平铺的字典，key 对应字段名，value 为基本类型（bool / int / float / string）

### 哪些字段应该同步

**应该同步：** 用户关心的功能开关、数值参数、路径

**不应该同步：** UI 临时状态（面板是否折叠、文本框缓存内容、窗口位置）

这些 UI 状态的取舍在 `ToCloudDict()` 里体现——不要把它们放进字典即可。

### 版本迁移策略

当设置结构发生变化（新增字段、字段改名、类型改变）：

1. 保留 `cloud_version` 字段
2. 读取时检查版本号，按旧格式兼容解析
3. 下次用户手动上传时，自动以新格式覆盖旧数据

示例：

```csharp
public static bool TryReadFromCloud(MySettings settings)
{
    // ... 读取 JSON ...

    string version = GetString(root, "cloud_version");

    if (version == "1.0.0")
    {
        // 旧格式：field 名称不同
        settings.Volume = GetFloat(dict, "masterVolume", 1f);  // 旧 key
    }
    else
    {
        settings.Volume = GetFloat(dict, "volume", 1f);        // 新 key
    }
}
```

---

## 降级策略

| 场景 | 行为 |
| --- | --- |
| Steam 未初始化 | 静默跳过，保持纯本地模式。UI 上显示「Steam 不可用」提示 |
| 云端文件不存在 | 下载时提示「云端没有存档」，上传时自动创建 |
| 云端 JSON 格式异常 | 不覆盖本地设置，日志记录错误 |
| 云端版本号缺失 | 仍然尝试读取，但不保证字段兼容 |
| `FileWrite` 失败 | 不抛异常，本地 `Settings.xml` 仍然有效 |

**原则：云端操作永远不应该影响本地功能的正常使用。**

---

## 本项目文件清单

| 文件 | 职责 |
| --- | --- |
| `src/Features/CloudSettings/CloudSettingsManager.cs` | 云同步核心逻辑：读写、序列化、版本检查 |
| `src/Settings.cs` | `ToCloudDict()` / `FromCloudDict()` 字段映射；`DrawCloudSyncSection()` UI 按钮；字段类型安全提取 |
| `src/Main.cs` | Mod 生命周期入口（不自动触发云同步） |
| `Resources/localization.json` | 云同步相关的 UI 文本（中英双语） |

### 依赖

- `com.rlabrecque.steamworks.net.dll` — 游戏自带，通过 `$(ManagedAssembliesDir)` 引用
- `GDMiniJSON` — 游戏内置的 JSON 序列化库
- `UnityModManager` — 设置持久化和日志

---

## 常见问题

**Q: Steam 有文件大小限制吗？**

Steam Cloud API 文档建议单个文件不超过 100KB。Mod 设置的 JSON 通常只有几百字节，完全在限制内。

**Q: 多个 Mod 如何避免文件名冲突？**

文件名直接用 Mod ID 前缀即可，例如 `ado_fai_editor_tweaks_settings`。Steam Cloud 的文件命名空间是所有游戏共享的，所以一定要带 Mod 前缀。

**Q: 云端数据和本地 Settings.xml 不一致时谁优先？**

本 Mod 的设计是本地优先：云端只作为备份/迁移通道，读取和写入都由用户手动触发。如果你实现了自动同步，建议在 Mod 加载时让云端覆盖本地（云端 = 最新设备上的配置）。

**Q: `GDMiniJSON` 的 `Json.Deserialize` 返回什么类型？**

返回 `object`，实际类型是 `Dictionary<string, object>` 或 `List<object>`。数字可能是 `int`、`long`、`double` 或 `float`，所以 `GetFloat` 等提取方法要处理多种类型。