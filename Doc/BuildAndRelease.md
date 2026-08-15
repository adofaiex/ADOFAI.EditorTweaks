# 构建与发行

目标框架为 `net481`，游戏依赖从 `GameExePath` 指向的安装目录读取。常用命令：

```powershell
dotnet build ADOFAI.EditorTweaks.csproj -c Debug
dotnet build ADOFAI.EditorTweaks.csproj -c Release
```

`ADOFAIMod.targets` 会编译 DLL、复制 `Info.json` 和本项目资源到 `out/`，生成 `Build/ADOFAI.EditorTweaks-1.4.7.zip`，并可部署到 `Mods/ADOFAI.EditorTweaks/`。本项目不构建 Web UI，也不复制第三方归档或视频依赖。
