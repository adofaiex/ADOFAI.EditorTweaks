# ADOFAI UnityMod 模板使用说明

## 1. 前置条件

请先安装：

1. Unity `6000.3.10f1`。
2. .NET SDK，并确保 `dotnet --version` 可以运行。
3. ADOFAI 本体。模板需要你本机的游戏 exe，不接受仓库里携带游戏文件。
4. UnityModManager。
5. Git。

ThunderKit 由 `Packages/manifest.json` 声明。第一次打开工程时，Unity 需要能够访问该依赖的 Git 地址。

## 2. 安装模板

如果已经克隆了模板仓库，在模板仓库根目录执行：

```powershell
dotnet new install .
dotnet new list
```

列表中应出现 `adofaimod`。

其他人可以先从 GitHub 克隆模板，再执行同样的安装命令：

```powershell
git clone https://github.com/StArraySharp/ADOFAI-UnityModTemplate.git
dotnet new install .\ADOFAI-UnityModTemplate
dotnet new list
```

`.NET` 模板安装命令不能直接把 GitHub URL 当作模板包；如果发布了 `.nupkg`，也可以下载后直接安装。

发布包也可以安装：

```powershell
dotnet new install .\artifacts\StArraySharp.EditorTweaksTemplate.1.0.0.nupkg
```

## 3. 创建项目

标准命令如下：

```powershell
dotnet new adofaimod `
  -n MyCoolMod `
  -o MyCoolMod `
  -g "C:\Games\ADOFAI\A Dance of Fire and Ice.exe" `
  --author-name "Your Name" `
  --description "My ADOFAI mod" `
  --version "1.0.0"
```

参数含义：

| 参数 | 作用 |
| --- | --- |
| `-n` / `--name` | 项目名、程序集名、命名空间、Mod ID |
| `-o` / `--output` | 生成目录；这是 `dotnet new` 自带参数 |
| `-g` / `--game-path` | ADOFAI exe 的完整路径 |
| `--author-name` | 写入 `Info.json` 和 ThunderKit Manifest；`-a/--author` 被 `dotnet new` 主程序保留 |
| `--description` | 写入 `Info.json` 的 `Description` 和 ThunderKit Manifest；包装脚本提供 `-d` |
| `--version` | 写入 `Info.json` 和 ThunderKit Manifest；包装脚本提供 `-v` |

项目名只能使用字母、数字和下划线，并且不能以数字开头。例如 `MyCoolMod`、`ADOFAI_Mod`、`Mod123` 合法；`My Cool Mod`、`my-mod`、`123Mod` 不合法。

仓库里的 `New-ADOFAIMod.ps1` 会在调用 `dotnet new` 前严格检查这些内容：

```powershell
.\New-ADOFAIMod.ps1 `
  -n MyCoolMod `
  -o MyCoolMod `
  -g "C:\Games\ADOFAI\A Dance of Fire and Ice.exe" `
  -a "Your Name" `
  -d "My ADOFAI mod" `
  -v "1.0.0"
```

## 4. 第一次打开 Unity

请用 Unity Hub 或 Unity Editor 打开生成项目的根目录，不要只打开 `Assets` 文件夹。

首次导入时会发生这些事情：

1. Unity 解析项目设置和 ThunderKit 包。
2. 模板脚本读取 `ProjectSettings/ADOFAI.Template.local.txt`。
3. 脚本检查 exe 是否存在，以及 `<游戏名>_Data/Managed` 是否存在。
4. 脚本自动写入 `Assets/ThunderKitSettings/ThunderKitSettings.asset` 的 `GamePath` 和 `GameExecutable`。
5. ThunderKit Settings 自动打开，并弹出提示。
6. 你点击一次 `Import`。

导入结束后，ThunderKit 会在本地生成：

```text
Packages/A Dance of Fire and Ice/
```

它提供 `Assembly-CSharp.dll`、`UnityModManager.dll` 等开发时需要的引用。它们来自你自己的游戏安装，不应放进模板仓库。

如果第一次打开时 ThunderKit 还没有加载完，等 Unity 完成编译后执行 `Tools > ADOFAI > Reset Template Bootstrap`，脚本会再次检查并配置。

## 5. 项目目录

```text
Assets/
├── Editor/
│   ├── TemplateBootstrap.cs
│   ├── TemplateBootstrap.Editor.asmdef
│   └── BuildMod.cs
├── Scenes/
├── Scripts/
│   ├── <ProjectName>.asmdef
│   ├── Main.cs
│   ├── ModSettings.cs
│   ├── Patches.cs
│   └── ResourceLoader.cs
└── Resources/
    ├── Prefabs/
    ├── Textures/
    ├── Materials/
    ├── Shaders/
    ├── Audio/
    ├── Fonts/
    ├── UI/
    ├── Animations/
    └── ScriptableObjects/
```

项目名会同时替换到：

- Unity `productName`
- `Assets/Scripts/<ProjectName>.asmdef` 的文件名和内部 `name`
- asmdef 的 `rootNamespace`
- C# 命名空间
- `Info.json` 的 `Id`、DLL 文件名和 `EntryMethod`
- ThunderKit Manifest 的 Mod 名称

Unity 打开项目后会自动生成 `.slnx`、`.csproj`、`Library`、`Temp` 等编辑器文件；这些不属于模板源代码，也已经被 `.gitignore` 排除。

## 6. 添加 Harmony 补丁

在 `Assets/Scripts/Patches.cs` 或其他脚本中添加补丁。例如：

```csharp
using HarmonyLib;

namespace MyCoolMod
{
    [HarmonyPatch(typeof(SomeGameType), nameof(SomeGameType.SomeMethod))]
    internal static class SomeMethodPatch
    {
        private static void Prefix()
        {
            // Your code here.
        }
    }
}
```

`Main.cs` 在 Mod 启用时执行当前程序集的 `PatchAll`，禁用时只撤销本 Mod 使用的 Harmony 补丁。

## 7. 添加资源

- Unity 场景放到 `Assets/Scenes/`。它们会进入 `scenes.assets`。
- Prefab 放到 `Assets/Resources/Prefabs/`。
- Texture 放到 `Assets/Resources/Textures/`。
- Material 放到 `Assets/Resources/Materials/`。
- Shader 放到 `Assets/Resources/Shaders/`。
- 音频放到 `Assets/Resources/Audio/`。
- 字体放到 `Assets/Resources/Fonts/`。
- UI 资源放到 `Assets/Resources/UI/`。
- Animation 资源放到 `Assets/Resources/Animations/`。
- ScriptableObject 放到 `Assets/Resources/ScriptableObjects/`。

`Assets/Resources/` 下的内容会进入 `resources.assets`。代码通过 `ResourceLoader.LoadAsset<T>(assetName)` 访问资源；模板不预设任何资源名称。

## 8. 构建和部署

1. 确认 ThunderKit 已经导入本机游戏包。
2. 在 Unity 中打开 `Tools > Build Mod`。
3. 选择 ThunderKit Pipeline。
4. 确认输出目录，默认是 `<ADOFAI目录>/Mods/`。
5. 点击 `Build Mod`。

ThunderKit 会构建程序集和两个资源包；模板窗口会把资源包和运行时资源统一复制到 Mod 的 `Resources/` 目录：

```text
<ProjectName>.dll
Info.json
Resources/
├── scenes.assets
├── resources.assets
├── localization.json
├── README.html
└── FFmpegReference.html
ThirdParty/
├── FFmpeg/ffmpeg.exe
└── 7-Zip/x64/7z.dll
SharpSevenZip.dll
```

构建失败会同时写入 Unity Console 并弹出错误窗口。窗口不会复制 ADOFAI 游戏 DLL，也不会启动游戏。

每次构建成功后，项目根目录的 `Build/` 下还会生成一份按 `Info.json` 版本号命名的完整 Mod 副本，例如 `Build/EditorTweaks-1.4.0/`。同一个版本再次构建时会更新原目录；修改 `Assets/Info.json` 中的 `Version` 后会生成新的版本目录。

## 9. 常见错误

### 游戏路径错误

`--game-path` 必须是 exe 文件本身，而不是游戏目录。确认旁边存在：

```text
<游戏 exe 名称去掉 .exe>_Data/Managed/
```

### Unity 版本不匹配

用 Unity `6000.3.10f1` 打开。不同版本可能改变包解析、程序集导入或 AssetBundle 构建结果。

### ThunderKit 没有生成游戏包

检查 ThunderKit Settings 的 `GamePath` 和 `GameExecutable`，然后点击 `Import`。如果配置窗口没有自动打开，执行 `Tools > ThunderKit > Settings`。

### asmdef 引用缺失

先完成 ThunderKit Import，再等待 Unity 编译。模板的 asmdef 依赖由游戏包提供；不要手动把 ADOFAI DLL 复制到仓库。

### Mod DLL 没有复制到游戏目录

确认输出目录是 ADOFAI 的 `Mods` 目录，并检查 ThunderKit Pipeline 生成了：

```text
ThunderKit/Libraries/<ProjectName>.dll
ThunderKit/AssetBundleStaging/scenes.assets
ThunderKit/AssetBundleStaging/resources.assets
```

如果缺少其中任何一个文件，构建窗口会停止并显示具体缺失项。

## 10. 为什么不能提交游戏 DLL

游戏 DLL 属于本机安装内容，会随着游戏版本、平台和安装位置变化。提交它们会让模板变大、绑定某台机器的游戏版本，并可能造成许可证和分发问题。ThunderKit 已经负责从用户自己的 ADOFAI 安装生成 Unity 可用的游戏包，所以模板只保留引用声明和导入配置。
