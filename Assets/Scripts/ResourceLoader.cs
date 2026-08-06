using System.IO;
using UnityEngine;

namespace EditorTweaks
{
    /// <summary>
    /// Loads and releases the AssetBundles produced by the ThunderKit pipeline.
    /// </summary>
    public static class ResourceLoader
    {
        private const string ScenesBundleName = "scenes.assets";
        private const string ResourcesBundleName = "resources.assets";
        private const string ModResourcesDirectory = "Resources";

        private static AssetBundle scenesBundle;
        private static AssetBundle resourcesBundle;

        public static bool LoadAll(string modPath)
        {
            UnloadAll();

            if (string.IsNullOrWhiteSpace(modPath))
            {
                Debug.LogError("Mod path is empty; AssetBundles cannot be loaded.");
                return false;
            }

            scenesBundle = LoadBundle(modPath, ScenesBundleName);
            resourcesBundle = LoadBundle(modPath, ResourcesBundleName);

            if (scenesBundle != null && resourcesBundle != null)
            {
                return true;
            }

            UnloadAll();
            return false;
        }

        public static T LoadAsset<T>(string assetName) where T : Object
        {
            if (resourcesBundle == null)
            {
                Debug.LogError("The resources AssetBundle is not loaded.");
                return null;
            }

            T asset = resourcesBundle.LoadAsset<T>(assetName);
            if (asset == null)
            {
                Debug.LogError("Asset not found in resources.assets: " + assetName);
            }

            return asset;
        }

        public static string[] GetScenePaths()
        {
            return scenesBundle == null ? new string[0] : scenesBundle.GetAllScenePaths();
        }

        public static void UnloadAll()
        {
            if (scenesBundle != null)
            {
                scenesBundle.Unload(true);
                scenesBundle = null;
            }

            if (resourcesBundle != null)
            {
                resourcesBundle.Unload(true);
                resourcesBundle = null;
            }
        }

        private static AssetBundle LoadBundle(string modPath, string bundleName)
        {
            string bundlePath = Path.Combine(modPath, ModResourcesDirectory, bundleName);
            if (!File.Exists(bundlePath))
            {
                Debug.LogError("AssetBundle not found: " + bundlePath);
                return null;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                Debug.LogError("AssetBundle failed to load: " + bundlePath);
            }

            return bundle;
        }
    }
}
