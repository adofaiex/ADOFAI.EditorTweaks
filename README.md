# ADOFAI UnityMod 模板

这是一个只服务于《A Dance of Fire and Ice》（ADOFAI）的 UnityModManager 模板。
它的目标是让你用一条 `dotnet new` 命令得到一个可以继续开发的 Unity 工程，而不是把某个作者电脑上的游戏 DLL 和缓存一起复制给你。

## 快速开始

前置条件：

- Windows
- Unity `6000.3.10f1`
- .NET SDK
- 已安装 ADOFAI、UnityModManager 和 Git
- 工程会通过 Packages 配置获取 ThunderKit

如果已经克隆了模板仓库，在仓库目录执行：

```powershell
dotnet new install .
dotnet new list
```

其他人可以从 GitHub 获取模板后安装：

```powershell
git clone https://github.com/StArraySharp/ADOFAI-UnityModTemplate.git
dotnet new install .\ADOFAI-UnityModTemplate
dotnet new list
```

`dotnet new install` 不能直接把 GitHub URL 当作模板包；也可以下载发布的 `.nupkg` 后直接安装。

然后创建 Mod：

```powershell
dotnet new adofaimod `
  --name MyCoolMod `
  --output MyCoolMod `
  --game-path "C:\Games\ADOFAI\A Dance of Fire and Ice.exe" `
  --author-name "Your Name" `
  --description "My ADOFAI mod" `
  --version "1.0.0"
```

项目名必须符合 C# 标识符规则：`^[A-Za-z_][A-Za-z0-9_]*$`。`.NET` 的 `dotnet new` 主程序已经占用了 `-a/--author` 作为模板筛选参数，所以原生命令使用 `--author-name`；仓库中的包装脚本仍提供计划中的 `-a` 写法，并会把它转换成正确的模板参数。如果希望在生成前同时检查项目名、游戏 exe 和 `_Data/Managed`，请使用包装脚本。

```powershell
.\New-ADOFAIMod.ps1 `
  -n MyCoolMod `
  -o MyCoolMod `
  -g "C:\Games\ADOFAI\A Dance of Fire and Ice.exe" `
  -a "Your Name" `
  -d "My ADOFAI mod" `
  -v "1.0.0"
```

完整初始化、目录说明和故障排查见 [docs/GettingStarted.md](docs/GettingStarted.md)。

## 第一次打开 Unity

打开生成目录后，Unity 会自己生成 `.slnx`、`.csproj`、`Library` 等编辑器产物，这些文件不属于模板源代码。

模板编辑器脚本会读取 `ProjectSettings/ADOFAI.Template.local.txt` 中的 exe 路径，检查游戏文件，并把它拆成 ThunderKit 需要的：

- `GamePath`：ADOFAI 所在目录
- `GameExecutable`：exe 文件名

随后会打开 ThunderKit Settings。你只需要点击一次 `Import`，让 ThunderKit 从本机游戏生成 `Packages/A Dance of Fire and Ice/`。这个目录包含本机游戏程序集，只在本机使用，不提交到 Git。

## 目录约定

```text
Assets/
├── Editor/                  编辑器初始化和构建工具
├── Scenes/                  以后放 Unity 场景，进入 scenes.assets
├── Scripts/                 Mod C# 代码和项目同名 asmdef
└── Resources/               以后放资源
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

模板不携带示例场景、示例资源或 Hello World 逻辑。`Main.cs` 负责 Mod 生命周期，`Patches.cs` 是 Harmony 补丁入口，`ResourceLoader.cs` 负责两个 AssetBundle 的加载和释放。

## 构建

Unity 完成 ThunderKit 导入后，在菜单中打开 `Tools > Build Mod`。选择 ThunderKit Pipeline 和输出目录，然后点击 `Build Mod`。默认输出目录是 ADOFAI 安装目录下的 `Mods/<ProjectName>/`。

构建窗口会把程序集、资源包和运行时文件部署到 Mod 目录：

```text
<ProjectName>.dll
Info.json
Resources/
├── scenes.assets
├── resources.assets       （存在 Unity 资源时生成）
├── localization.json
├── README.html
└── FFmpegReference.html
ThirdParty/
├── FFmpeg/ffmpeg.exe
└── 7-Zip/x64/7z.dll
SharpSevenZip.dll
```

它不会复制游戏 DLL，也不会自动启动游戏。

每次构建成功后，还会在项目根目录生成一个按版本号命名的完整 Mod 副本，例如：

```text
Build/
└── EditorTweaks-1.4.0/
    ├── EditorTweaks.dll
    ├── Info.json
    ├── Resources/
    ├── ThirdParty/
    └── SharpSevenZip.dll
```

版本号来自 `Assets/Info.json`。同一个版本再次构建时，会更新 `Build/EditorTweaks-1.4.0/`；修改 `Info.json` 的版本号后，则会生成新的版本目录。

## 打包模板

直接从 Git 仓库安装：

```powershell
dotnet new install .
```

生成 NuGet 模板包：

```powershell
dotnet pack .\ADOFAIModTemplate.Template.csproj -c Release
dotnet new install .\artifacts\StArraySharp.EditorTweaksTemplate.1.0.0.nupkg
```

打包时会排除 Git、Unity 缓存、ThunderKit 构建输出、本机游戏包、模板本身的打包辅助文件和本地游戏路径配置。

## 参考

- [.NET 自定义模板文档](https://learn.microsoft.com/zh-cn/dotnet/core/tools/custom-templates)
- [ThunderKit 导入流程](https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/ThunderKit/Crash-Course-and-Getting-Started/)
