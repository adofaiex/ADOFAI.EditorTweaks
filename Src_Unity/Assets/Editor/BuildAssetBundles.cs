using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EditorTweaks.Build
{
    /// <summary>
    /// 把 Assets/EditorTweaksUI 下的全部资源打成一个 AssetBundle，输出到仓库根目录的 Resources/。
    /// 菜单：EditorTweaks/Build AssetBundles
    /// 命令行：Unity.exe -batchmode -quit -projectPath Src_Unity -executeMethod EditorTweaks.Build.BuildAssetBundles.BuildFromCommandLine
    /// </summary>
    public static class BuildAssetBundles
    {
        private const string SourceFolder = "Assets/EditorTweaksUI";
        private const string BundleName = "editortweaks.ui";

        private static string OutputFolder =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Resources"));

        [MenuItem("EditorTweaks/Build AssetBundles")]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Build()
        {
            if (!AssetDatabase.IsValidFolder(SourceFolder))
            {
                Debug.LogWarning("[EditorTweaks] 找不到 " + SourceFolder + "，没有可打包的资源。");
                return;
            }

            string[] assets = AssetDatabase.FindAssets("", new[] { SourceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .ToArray();
            if (assets.Length == 0)
            {
                Debug.LogWarning("[EditorTweaks] " + SourceFolder + " 里没有资源。");
                return;
            }

            Directory.CreateDirectory(OutputFolder);
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                OutputFolder,
                new[] { new AssetBundleBuild { assetBundleName = BundleName, assetNames = assets } },
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
            {
                throw new InvalidOperationException("AssetBundle 构建失败，详见控制台。");
            }

            // 只保留 bundle 本身，清掉 Unity 额外生成的清单文件。
            foreach (string file in Directory.GetFiles(OutputFolder, "*.manifest"))
            {
                File.Delete(file);
            }

            string folderManifest = Path.Combine(OutputFolder, Path.GetFileName(OutputFolder));
            if (File.Exists(folderManifest))
            {
                File.Delete(folderManifest);
            }

            Debug.Log("[EditorTweaks] 已输出 " + Path.Combine(OutputFolder, BundleName) + "（" + assets.Length + " 个资源）");
        }
    }
}
