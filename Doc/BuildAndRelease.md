# 构建与发行

## 本地构建

项目是 `net481` 类库，编译时需要 ADOFAI 本体的 `*_Data/Managed` 目录。项目文件通过 `GameExePath` 推导依赖目录，因此本地机器必须安装对应版本的游戏。

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

正式发行时不要使用会自动递增版本号的 `build-release.bat`，除非你确实要在本地修改 `Info.json`。发布版本应先提交正确的 `Info.json` 和 `CHANGELOG.md`，再创建同名 Git 标签。

## GitHub Actions 自动构建

工作流文件是 `.github/workflows/release.yml`。它支持两种入口：

1. 推送版本标签后自动构建并创建 GitHub Release。
2. 在 GitHub Actions 页面手动输入已有标签，补构建历史版本。

项目依赖 ADOFAI、UnityModManager 和 Steamworks 的 DLL，这些文件不能放进公开仓库，所以工作流使用 Windows self-hosted runner，而不是普通的 GitHub-hosted runner。

### 配置 Runner

在仓库 GitHub 页面进入 `Settings → Actions → Runners → New self-hosted runner`，在一台安装了 ADOFAI 和对应版本游戏依赖的 Windows x64 电脑上安装并保持 Runner 在线。默认标签 `self-hosted`、`windows`、`x64` 即可匹配工作流。

默认游戏路径是：

```text
D:\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice.exe
```

如果路径不同，在仓库 `Settings → Secrets and variables → Actions → Variables` 中添加：

```text
ADOFAI_GAME_EXE_PATH
```

历史版本如果使用不同的游戏版本，可以在手动运行工作流时填写 `game_exe_path`，工作流会检查该版本所需的 Managed DLL 是否完整。

### 发布新版本

例如当前代码已经准备好 `1.2.8`：

```powershell
git tag -a 1.2.8 -m "发布 1.2.8"
git push origin 1.2.8
```

推送标签后，Actions 会自动：

1. 检出该标签对应的代码。
2. 检查标签版本与 `Info.json` 是否一致。
3. 使用游戏 Managed DLL 编译并生成 ZIP。
4. 从 `CHANGELOG.md` 提取对应版本的用户更新日志。
5. 创建 GitHub Release 并上传 ZIP。

### 补构建历史版本

进入 `Actions → Build and publish release → Run workflow`，在 `tag` 中依次输入已有标签，例如：

```text
v1.0.0
1.1.0
1.2.0
1.2.1
1.2.2
1.2.3
1.2.4
1.2.5
1.2.6
1.2.7
```

每个标签单独运行一次。历史版本必须使用与该版本兼容的游戏 Managed DLL；如果当前游戏已经更新，工作流会在构建前明确列出缺失 DLL，而不是生成错误版本的包。

补构建历史版本时，工作流不会把旧版本标记为 GitHub 的 Latest Release；正常推送新标签时才会更新 Latest Release。
