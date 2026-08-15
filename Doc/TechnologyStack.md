# 技术栈

- C# / .NET Framework 4.8.1。
- UnityModManager 提供入口、设置保存和 UMM 面板。
- Harmony 负责对游戏方法做小范围前置或后置补丁。
- UnityEngine、Assembly-CSharp 和游戏 Managed 目录中的模块 DLL 提供运行时类型。

构建不引用另外两个 Mod 的程序集。项目输出只有 `ADOFAI.EditorTweaks.dll`、`Info.json` 和本项目资源。
