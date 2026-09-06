# 构建与发行

目标框架为 `net481`，游戏依赖从 `GameExePath` 指向的安装目录读取。常用命令：

```powershell
dotnet build ADOFAI.EditorTweaks.csproj -c Debug
dotnet build ADOFAI.EditorTweaks.csproj -c Release
```

`ADOFAIMod.targets` 会编译 DLL、复制 `Info.json` 和本项目资源到 `out/`，生成 `Build/ADOFAI.EditorTweaks-1.4.8.zip`，并可部署到 `Mods/ADOFAI.EditorTweaks/`。本项目不构建 Web UI，也不复制第三方归档或视频依赖。

## GitHub Actions

`.github/workflows/build.yml` 会从私有仓库 `adofaiex/ADOFAI.GameAssemblies` 的 `game-assemblies-2026.09.06.2` 标签读取游戏程序集，然后执行 Release 构建。

主仓库需要配置一个名为 `GAME_ASSEMBLIES_READ_TOKEN` 的 Secret，用于读取程序集私有仓库。也可以配置以下仓库变量：

- `GAME_ASSEMBLIES_REPOSITORY`：程序集仓库名称。
- `GAME_ASSEMBLIES_REF`：程序集仓库的分支或标签。

云端构建会关闭游戏 EXE 检查和本机 Mod 部署，只上传最终的 Mod 压缩包。
