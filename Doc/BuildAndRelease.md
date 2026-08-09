# 构建与发行

## 本地构建

项目是 `net481` 类库，编译时直接引用本机 ADOFAI 安装目录中的 `*_Data/Managed`。项目通过 `GameExePath` 定位游戏，不会把游戏 DLL 复制到项目目录，也不会把它们放进最终 Mod 包。

如果游戏不在项目文件中的默认路径，可以通过 `/p:GameExePath` 指定游戏可执行文件：

```powershell
dotnet build ADOFAI.EditorTweaks.csproj -c Debug /p:GameExePath="D:\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice.exe"
```

开发构建：

```bat
build-dev.bat
```

正式构建：

```powershell
dotnet build ADOFAI.EditorTweaks.csproj -c Release /p:CreateModPackage=true /p:BumpModVersion=false /p:AutoLaunchGame=false
```

构建会生成：

- `Build/ADOFAI.EditorTweaks-<Version>/`
- `Build/ADOFAI.EditorTweaks-<Version>.zip`

构建必须在安装了对应版本游戏的 Windows 机器上执行。正式发行时不要使用会自动递增版本号的 `build-release.bat`，除非你确实要在本地修改 `Info.json`。发布版本应先提交正确的 `Info.json` 和 `CHANGELOG.md`，再创建同名 Git 标签。

### 最终包结构检查

发行前至少确认以下文件进入 `out/`、Build 目录和最终 ZIP：

```text
ADOFAI.EditorTweaks.dll
Info.json
SharpSevenZip.dll
Resources/
├── README.html
├── FFmpegReference.html
└── localization.json
ThirdParty/
├── 7-Zip/
│   ├── License.txt
│   └── x64/7z.dll
├── FFmpeg/
│   ├── ffmpeg.exe
│   ├── FFmpeg-BUILD.txt
│   ├── FFmpeg-SOURCE.txt
│   ├── FFmpeg-NOTICE.txt
│   └── GPL-3.0.txt
└── SharpSevenZip/LICENSE.txt
```

源代码、游戏 Managed 目录、工作区、渲染临时文件和历史 Build 目录不得混入发布包。`SharpSevenZip.dll` 必须位于 Mod 根目录；FFmpeg 和 `7z.dll` 统一放在 `ThirdParty/` 下，其中 `7z.dll` 必须保持在 `ThirdParty/7-Zip/x64/`，运行时按这些相对位置加载。

## 本地发行流程

发行构建必须在安装了游戏的本机执行。先确认 `Info.json` 中的版本号正确，再运行：

```powershell
dotnet build ADOFAI.EditorTweaks.csproj `
  -c Release `
  /p:CreateModPackage=true `
  /p:BumpModVersion=false `
  /p:AutoLaunchGame=false `
  /p:GameExePath="D:\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice.exe"
```

构建完成后，检查 `Build/ADOFAI.EditorTweaks-<Version>.zip`，然后手动创建 Git 标签并上传发行包：

```powershell
git tag -a <version> -m "发布 <version>"
git push origin <version>
```

GitHub Actions 不再负责构建或发布；GitHub Release 需要使用已经生成的 ZIP 手动创建或上传。
