using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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

        // Src/UI/SettingsWindow.cs 会按名字在 Settings.uxml 里查找这些元素，少一个窗口就建不出来
        private static readonly string[] RequiredNames =
        {
            "overlay", "backdrop", "window", "head", "logo", "titles", "tag", "x", "body", "cats", "cat-hl",
            "page", "scroll", "foot", "reset", "reset-icon", "reset-label", "close", "toast", "toast-label", "arm-a", "arm-b"
        };

        [MenuItem("EditorTweaks/Build AssetBundles")]
        public static void BuildFromMenu()
        {
            Build();
            Verify();
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                Verify();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>把刚打好的包加载一遍：PanelSettings、主题、布局都在，并且布局里有代码要找的全部名字。</summary>
        private static void Verify()
        {
            string path = Path.Combine(OutputFolder, BundleName);
            if (!File.Exists(path)) return;

            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException("打好的包加载失败：" + path);
            try
            {
                var panel = bundle.LoadAsset<PanelSettings>(SourceFolder + "/EditorTweaksPanel.asset");
                if (panel == null) throw new InvalidOperationException("包里没有 PanelSettings");
                if (panel.themeStyleSheet == null) throw new InvalidOperationException("PanelSettings 没有绑主题");

                var layout = bundle.LoadAsset<VisualTreeAsset>(SourceFolder + "/Settings.uxml");
                if (layout == null) throw new InvalidOperationException("包里没有 Settings.uxml");

                VisualElement root = layout.Instantiate();
                string[] missing = RequiredNames.Where(name => root.Q(name) == null).ToArray();
                if (missing.Length > 0) throw new InvalidOperationException("Settings.uxml 缺少元素：" + string.Join(", ", missing));

                Debug.Log("[EditorTweaks] 自检通过：PanelSettings、主题、布局（" + RequiredNames.Length + " 个必需元素）都在。");
            }
            finally
            {
                bundle.Unload(true);
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
