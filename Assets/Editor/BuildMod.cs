using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ThunderKit.Core.Manifests.Datums;
using ThunderKit.Core.Pipelines;
using UnityEditor;
using UnityEngine;

namespace EditorTweaks.Editor
{
    /// <summary>
    /// Runs the selected ThunderKit pipeline and deploys only this mod's outputs.
    /// ThunderKit remains responsible for importing game assemblies and building assets.
    /// </summary>
    public sealed class BuildMod : EditorWindow
    {
        private const string ExpectedModId = "EditorTweaks";
        private const string ModsPathPreference = "EditorTweaks.BuildMod.ModsPath";
        private const string PipelinePreference = "EditorTweaks.BuildMod.Pipeline";
        private const string ThunderKitSettingsPath = "Assets/ThunderKitSettings/ThunderKitSettings.asset";
        private const string RuntimeFilesDirectory = "ModRuntime";
        private const string BuildOutputDirectory = "Build";
        private const string ModResourcesDirectory = "Resources";
        private const string ScenesBundleName = "scenes.assets";
        private const string ResourcesBundleName = "resources.assets";

        private string modsPath;
        private Pipeline selectedPipeline;
        private bool isBuilding;

        [MenuItem("Tools/Build Mod")]
        public static void ShowWindow()
        {
            GetWindow<BuildMod>("Build Mod");
        }

        public static void BuildFromCommandLine()
        {
            BuildMod builder = CreateInstance<BuildMod>();
            builder.modsPath = GetDefaultModsPath();
            builder.selectedPipeline = AssetDatabase.LoadAssetAtPath<Pipeline>(
                "Assets/ThunderKitSettings/PipeLine/Pipeline.asset");

            if (builder.selectedPipeline == null)
            {
                Debug.LogError(
                    "The default ThunderKit pipeline was not found at Assets/ThunderKitSettings/PipeLine/Pipeline.asset.");
                DestroyImmediate(builder);
                EditorApplication.Exit(1);
                return;
            }

            BuildFromCommandLineAsync(builder);
        }

        private static async void BuildFromCommandLineAsync(BuildMod builder)
        {
            try
            {
                await builder.BuildModFunction();
                string modOutputPath = Path.Combine(
                    builder.modsPath,
                    builder.selectedPipeline.manifest.Identity.Name);
                ValidateOutput(
                    modOutputPath,
                    ExpectedModId,
                    HasBundleInputs(builder.selectedPipeline, "resources.assets"));
                Debug.Log("Command-line Mod build completed: " + modOutputPath);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                DestroyImmediate(builder);
            }
        }

        private void OnEnable()
        {
            modsPath = EditorPrefs.GetString(ModsPathPreference, GetDefaultModsPath());
            string pipelinePath = EditorPrefs.GetString(PipelinePreference, string.Empty);
            if (!string.IsNullOrEmpty(pipelinePath))
            {
                selectedPipeline = AssetDatabase.LoadAssetAtPath<Pipeline>(pipelinePath);
            }
        }

        private void OnDisable()
        {
            SavePreferences();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Build and deploy the Mod through ThunderKit.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                modsPath = EditorGUILayout.TextField("Mods Directory", modsPath);
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    string chosenPath = EditorUtility.OpenFolderPanel("Select ADOFAI Mods Directory", modsPath, string.Empty);
                    if (!string.IsNullOrEmpty(chosenPath))
                    {
                        modsPath = chosenPath;
                        SavePreferences();
                    }
                }
            }

            selectedPipeline = (Pipeline)EditorGUILayout.ObjectField(
                "ThunderKit Pipeline",
                selectedPipeline,
                typeof(Pipeline),
                false);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The pipeline imports the local game package and builds scenes.assets, resources.assets when Unity resources are assigned, and the mod assembly. Runtime resources and third-party files from ModRuntime are copied into the Mod output. This window does not copy game DLLs or start the game.",
                MessageType.Info);

            EditorGUI.BeginDisabledGroup(isBuilding || string.IsNullOrWhiteSpace(modsPath) || selectedPipeline == null);
            if (GUILayout.Button(isBuilding ? "Building..." : "Build Mod", GUILayout.Height(32)))
            {
                _ = BuildModFunction();
            }
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrWhiteSpace(modsPath))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Output", modsPath);
            }

            if (selectedPipeline != null)
            {
                EditorGUILayout.LabelField("Pipeline", selectedPipeline.name);
            }
        }

        private async Task BuildModFunction()
        {
            if (isBuilding)
            {
                return;
            }

            isBuilding = true;
            try
            {
                SavePreferences();
                ValidateConfiguration();

                await selectedPipeline.Execute();

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string thunderKitOutput = Path.Combine(projectRoot, "ThunderKit");
                string stagingPath = Path.Combine(thunderKitOutput, "AssetBundleStaging");
                string librariesPath = Path.Combine(thunderKitOutput, "Libraries");
                string modId = selectedPipeline.manifest.Identity.Name;

                string infoPath = Path.Combine(Application.dataPath, "Info.json");
                string assemblyPath = FindExactFile(librariesPath, modId + ".dll");
                string scenesBundlePath = FindExactFile(stagingPath, ScenesBundleName);
                string resourcesBundlePath = FindExactFile(stagingPath, ResourcesBundleName);
                bool resourcesBundleRequired = HasBundleInputs(selectedPipeline, ResourcesBundleName);
                string runtimeFilesPath = Path.Combine(projectRoot, RuntimeFilesDirectory);

                RequireFile(infoPath, "Info.json");
                ModInfo info = ReadModInfo(infoPath, modId);
                RequireFile(assemblyPath, modId + ".dll");
                RequireFile(scenesBundlePath, ScenesBundleName);
                if (resourcesBundleRequired)
                {
                    RequireFile(resourcesBundlePath, ResourcesBundleName);
                }
                else if (!string.IsNullOrEmpty(resourcesBundlePath))
                {
                    Debug.Log("No Unity assets are assigned to resources.assets; omitting the empty bundle.");
                }

                string modOutputPath = Path.Combine(modsPath, modId);
                string modResourcesPath = Path.Combine(modOutputPath, ModResourcesDirectory);
                Directory.CreateDirectory(modOutputPath);

                File.Copy(infoPath, Path.Combine(modOutputPath, "Info.json"), true);
                File.Copy(assemblyPath, Path.Combine(modOutputPath, modId + ".dll"), true);
                CopyRuntimeFiles(runtimeFilesPath, modOutputPath);

                Directory.CreateDirectory(modResourcesPath);
                File.Copy(scenesBundlePath, Path.Combine(modResourcesPath, ScenesBundleName), true);
                if (!string.IsNullOrEmpty(resourcesBundlePath))
                {
                    File.Copy(resourcesBundlePath, Path.Combine(modResourcesPath, ResourcesBundleName), true);
                }
                else
                {
                    DeleteIfExists(Path.Combine(modResourcesPath, ResourcesBundleName));
                }

                DeleteIfExists(Path.Combine(modOutputPath, ScenesBundleName));
                DeleteIfExists(Path.Combine(modOutputPath, ResourcesBundleName));
                DeleteIfExists(Path.Combine(modOutputPath, "Tools", "ffmpeg.exe"));
                DeleteDirectoryIfEmpty(Path.Combine(modOutputPath, "Tools"));

                ValidateOutput(modOutputPath, modId, resourcesBundleRequired);
                string versionedBuildPath = CreateVersionedBuild(
                    projectRoot,
                    modOutputPath,
                    modId,
                    info.Version);
                Debug.Log("ADOFAI Mod build succeeded: " + modOutputPath);
                Debug.Log("Versioned Mod package created: " + versionedBuildPath);
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog(
                        "Build Mod",
                        "Build succeeded.\n\nGame output:\n" + modOutputPath
                        + "\n\nVersioned package:\n" + versionedBuildPath,
                        "OK");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("Build Mod Failed", exception.Message, "OK");
                }
            }
            finally
            {
                isBuilding = false;
                Repaint();
            }
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(modsPath))
            {
                throw new InvalidOperationException("Choose an ADOFAI Mods directory before building.");
            }

            if (selectedPipeline == null)
            {
                throw new InvalidOperationException("Choose a ThunderKit Pipeline before building.");
            }

            if (selectedPipeline.manifest == null || selectedPipeline.manifest.Identity == null)
            {
                throw new InvalidOperationException("The selected ThunderKit Pipeline has no Manifest identity.");
            }

            string modId = selectedPipeline.manifest.Identity.Name;
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new InvalidOperationException("The ThunderKit Manifest name is empty.");
            }

            if (!string.Equals(modId, ExpectedModId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The ThunderKit Manifest name must be '" + ExpectedModId + "' but was '" + modId + "'.");
            }
        }

        private static string FindExactFile(string rootPath, string fileName)
        {
            if (!Directory.Exists(rootPath))
            {
                return null;
            }

            string[] matches = Directory.GetFiles(rootPath, fileName, SearchOption.AllDirectories);
            return matches.Length == 1 ? matches[0] : null;
        }

        private static void RequireFile(string path, string description)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new FileNotFoundException(
                    "ThunderKit did not produce the required output: " + description +
                    ". Run the ThunderKit import/build steps and try again.",
                    path);
            }
        }

        private static void ValidateOutput(string outputPath, string modId, bool requireResourcesBundle)
        {
            RequireFile(Path.Combine(outputPath, modId + ".dll"), modId + ".dll");
            RequireFile(Path.Combine(outputPath, "Info.json"), "Info.json");
            RequireFile(Path.Combine(outputPath, ModResourcesDirectory, ScenesBundleName), ScenesBundleName);
            if (requireResourcesBundle)
            {
                RequireFile(Path.Combine(outputPath, ModResourcesDirectory, ResourcesBundleName), ResourcesBundleName);
            }
            RequireFile(Path.Combine(outputPath, ModResourcesDirectory, "localization.json"), "Resources/localization.json");
            RequireFile(Path.Combine(outputPath, ModResourcesDirectory, "README.html"), "Resources/README.html");
            RequireFile(Path.Combine(outputPath, ModResourcesDirectory, "FFmpegReference.html"), "Resources/FFmpegReference.html");
            RequireFile(
                Path.Combine(outputPath, "ThirdParty", "FFmpeg", "ffmpeg.exe"),
                "ThirdParty/FFmpeg/ffmpeg.exe");
            RequireFile(Path.Combine(outputPath, "ThirdParty", "7-Zip", "x64", "7z.dll"), "ThirdParty/7-Zip/x64/7z.dll");
            RequireFile(Path.Combine(outputPath, "SharpSevenZip.dll"), "SharpSevenZip.dll");

            ReadModInfo(Path.Combine(outputPath, "Info.json"), modId);
        }

        private static ModInfo ReadModInfo(string infoPath, string modId)
        {
            string infoText = File.ReadAllText(infoPath);
            ModInfo info = JsonUtility.FromJson<ModInfo>(infoText);
            if (info == null
                || !string.Equals(info.Id, modId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(info.Version)
                || info.Version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !string.Equals(info.AssemblyName, modId + ".dll", StringComparison.Ordinal)
                || !string.Equals(info.EntryMethod, "EditorTweaks.Main.Load", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Info.json does not match the expected EditorTweaks Mod identity, version, or entry method.");
            }

            return info;
        }

        private static string CreateVersionedBuild(
            string projectRoot,
            string sourcePath,
            string modId,
            string version)
        {
            string buildRoot = Path.GetFullPath(Path.Combine(projectRoot, BuildOutputDirectory));
            string packagePath = Path.GetFullPath(Path.Combine(buildRoot, modId + "-" + version));
            string buildRootWithSeparator = buildRoot.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!packagePath.StartsWith(buildRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The versioned package path is outside the project's Build directory.");
            }

            Directory.CreateDirectory(buildRoot);
            if (Directory.Exists(packagePath))
            {
                Directory.Delete(packagePath, true);
            }

            CopyDirectoryContents(sourcePath, packagePath);
            return packagePath;
        }

        private static bool HasBundleInputs(Pipeline pipeline, string bundleName)
        {
            return pipeline.manifest.Data
                .OfType<AssetBundleDefinitions>()
                .SelectMany(definitions => definitions.assetBundles ?? Array.Empty<AssetBundleDefinition>())
                .Where(definition => string.Equals(definition.assetBundleName, bundleName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(definition => definition.assets ?? Array.Empty<UnityEngine.Object>())
                .Any(HasImportableAssets);
        }

        private static bool HasImportableAssets(UnityEngine.Object asset)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                return true;
            }

            string absolutePath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(absolutePath)
                && Directory.GetFiles(absolutePath, "*", SearchOption.AllDirectories)
                    .Select(path => path.Replace(Path.DirectorySeparatorChar, '/'))
                    .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    .Where(path => !Path.GetFileName(path).Equals(".gitkeep", StringComparison.OrdinalIgnoreCase))
                    .Any(path => AssetDatabase.LoadMainAssetAtPath(path) != null);
        }

        private static void CopyRuntimeFiles(string runtimeRoot, string outputPath)
        {
            if (!Directory.Exists(runtimeRoot))
            {
                throw new DirectoryNotFoundException(
                    "Runtime file directory was not found: " + runtimeRoot);
            }

            CopyDirectoryContents(
                Path.Combine(runtimeRoot, "Resources"),
                Path.Combine(outputPath, "Resources"));
            CopyDirectoryContents(
                Path.Combine(runtimeRoot, "ThirdParty"),
                Path.Combine(outputPath, "ThirdParty"));
            CopyDirectoryContents(
                Path.Combine(runtimeRoot, "Managed"),
                outputPath);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void DeleteDirectoryIfEmpty(string path)
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }

        private static void CopyDirectoryContents(string sourcePath, string destinationPath)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException(
                    "Required runtime directory was not found: " + sourcePath);
            }

            Directory.CreateDirectory(destinationPath);

            foreach (string filePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourcePath, filePath);
                string destinationFilePath = Path.Combine(destinationPath, relativePath);
                string destinationDirectory = Path.GetDirectoryName(destinationFilePath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(filePath, destinationFilePath, true);
            }
        }

        [Serializable]
        private sealed class ModInfo
        {
            public string Id;
            public string Version;
            public string AssemblyName;
            public string EntryMethod;
        }

        private static string GetDefaultModsPath()
        {
            UnityEngine.Object settingsAsset = AssetDatabase.LoadMainAssetAtPath(ThunderKitSettingsPath);
            if (settingsAsset == null)
            {
                return string.Empty;
            }

            SerializedObject settings = new SerializedObject(settingsAsset);
            SerializedProperty gamePath = settings.FindProperty("GamePath");
            if (gamePath == null || string.IsNullOrWhiteSpace(gamePath.stringValue))
            {
                return string.Empty;
            }

            return Path.Combine(gamePath.stringValue, "Mods");
        }

        private void SavePreferences()
        {
            EditorPrefs.SetString(ModsPathPreference, modsPath ?? string.Empty);
            if (selectedPipeline == null)
            {
                EditorPrefs.DeleteKey(PipelinePreference);
                return;
            }

            EditorPrefs.SetString(PipelinePreference, AssetDatabase.GetAssetPath(selectedPipeline));
        }
    }
}
