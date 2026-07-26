# 构建与发行

## 本地构建

项目是 `net481` 类库，编译引用统一放在项目根目录的 `lib/`。本机如果安装了 ADOFAI，项目会通过 `GameExePath` 找到游戏的 `*_Data/Managed` 目录，并在每次构建前把 DLL 同步到 `lib/`；没有安装游戏时，只要 `lib/` 已经存在，也可以直接编译。

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

第一次在安装了游戏的机器上构建时会自动生成并同步 `lib/`。这些 DLL 是编译引用，不会被复制进最终 Mod 包。正式发行时不要使用会自动递增版本号的 `build-release.bat`，除非你确实要在本地修改 `Info.json`。发布版本应先提交正确的 `Info.json` 和 `CHANGELOG.md`，再创建同名 Git 标签。

## GitHub Actions 自动构建

工作流文件是 `.github/workflows/release.yml`。它支持两种入口：

1. 推送版本标签后自动构建并创建 GitHub Release。
2. 在 GitHub Actions 页面手动输入已有标签，补构建历史版本。

项目依赖的 ADOFAI、UnityModManager 和 Steamworks DLL 已同步到公开仓库的 `lib/`，因此 GitHub Actions 可以直接使用 `windows-latest` 构建，不需要 self-hosted runner，也不需要 GitHub 机器安装游戏。

### 发布新版本

例如当前代码已经准备好 `1.3.0`：

```powershell
git tag -a 1.3.0 -m "发布 1.3.0"
git push origin 1.3.0
```

推送标签后，Actions 会自动：

1. 检出该标签对应的代码。
2. 检查标签版本与 `Info.json` 是否一致。
3. 使用仓库 `lib/` 中的 DLL 编译并生成 ZIP。
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

每个标签单独运行一次。

补构建历史版本时，工作流不会把旧版本标记为 GitHub 的 Latest Release；正常推送新标签时才会更新 Latest Release。
