using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EditorTweaks.Editor
{
    // Keep the editor assembly import path active when build tooling changes.
    /// <summary>
    /// Converts the full executable path supplied to dotnet new into ThunderKit's
    /// separate GamePath and GameExecutable settings.
    /// </summary>
    [InitializeOnLoad]
    internal static class TemplateBootstrap
    {
        private const string LocalConfigurationFile = "ProjectSettings/ADOFAI.Template.local.txt";
        private const string ThunderKitSettingsAsset = "Assets/ThunderKitSettings/ThunderKitSettings.asset";
        private const string GamePathPlaceholder = "__GAME_EXE_" + "PATH__";
        private const string GamePackageDirectory = "Packages/A Dance of Fire and Ice";
        private const string GameAssemblyDefine = "ADOFAI_GAME_IMPORTED";

        static TemplateBootstrap()
        {
            EditorApplication.delayCall += OnEditorReady;
        }

        private static void OnEditorReady()
        {
            Initialize();
            StartGamePackageWatch();
        }

        [MenuItem("Tools/ADOFAI/Reset Template Bootstrap")]
        private static void ResetBootstrap()
        {
            EditorPrefs.DeleteKey(GetInitializationPreferenceKey());
            Initialize();
        }

        private static void Initialize()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string configurationPath = Path.Combine(projectRoot, LocalConfigurationFile);
            if (!File.Exists(configurationPath))
            {
                return;
            }

            string executablePath = File.ReadAllText(configurationPath).Trim();
            if (string.IsNullOrWhiteSpace(executablePath) || executablePath == GamePathPlaceholder)
            {
                ShowError("The template has no ADOFAI executable path. Recreate the project with --game-path set to the full ADOFAI .exe path.");
                return;
            }

            if (!File.Exists(executablePath))
            {
                ShowError("The configured ADOFAI executable does not exist:\n\n" + executablePath);
                return;
            }

            string gameDirectory = Path.GetDirectoryName(executablePath);
            string executableName = Path.GetFileName(executablePath);
            string managedDirectory = Path.Combine(
                gameDirectory,
                Path.GetFileNameWithoutExtension(executableName) + "_Data",
                "Managed");

            if (!Directory.Exists(managedDirectory))
            {
                ShowError(
                    "The ADOFAI Managed directory was not found. Check that --game-path points to the real game executable:\n\n" +
                    managedDirectory);
                return;
            }

            string initializationKey = GetInitializationPreferenceKey();
            if (EditorPrefs.GetBool(initializationKey, false))
            {
                return;
            }

            UnityEngine.Object settingsAsset = AssetDatabase.LoadMainAssetAtPath(ThunderKitSettingsAsset);
            if (settingsAsset == null)
            {
                ShowError("ThunderKitSettings.asset could not be found. Let Unity finish importing packages, then use Tools > ADOFAI > Reset Template Bootstrap.");
                return;
            }

            SerializedObject settings = new SerializedObject(settingsAsset);
            SerializedProperty gamePathProperty = settings.FindProperty("GamePath");
            SerializedProperty gameExecutableProperty = settings.FindProperty("GameExecutable");
            SerializedProperty showOnStartupProperty = settings.FindProperty("ShowOnStartup");
            if (gamePathProperty == null || gameExecutableProperty == null)
            {
                ShowError("This ThunderKit version does not expose the expected GamePath/GameExecutable settings.");
                return;
            }

            gamePathProperty.stringValue = gameDirectory;
            gameExecutableProperty.stringValue = executableName;
            if (showOnStartupProperty != null)
            {
                showOnStartupProperty.boolValue = true;
            }

            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            EditorPrefs.SetBool(initializationKey, true);

            bool settingsOpened = EditorApplication.ExecuteMenuItem("Tools/ThunderKit/Settings");
            string message = settingsOpened
                ? "ThunderKit has been configured for this ADOFAI installation. Click Import once in the ThunderKit Settings window."
                : "ThunderKit has been configured for this ADOFAI installation. Open Tools > ThunderKit > Settings, then click Import once.";
            EditorUtility.DisplayDialog("ADOFAI Mod Template", message, "OK");
        }

        private static void StartGamePackageWatch()
        {
            if (HasGameAssemblyDefine())
            {
                return;
            }

            EditorApplication.update -= WaitForGamePackageImport;
            EditorApplication.update += WaitForGamePackageImport;
        }

        private static void WaitForGamePackageImport()
        {
            if (!IsGamePackageImported())
            {
                return;
            }

            EditorApplication.update -= WaitForGamePackageImport;
            AddGameAssemblyDefine();
            Debug.Log("ADOFAI game assemblies detected. Runtime Mod assembly compilation has been enabled.");
        }

        private static bool IsGamePackageImported()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string packageDirectory = Path.Combine(projectRoot, GamePackageDirectory);
            if (!Directory.Exists(packageDirectory))
            {
                return false;
            }

            return Directory.GetFiles(packageDirectory, "Assembly-CSharp.dll", SearchOption.AllDirectories).Length > 0;
        }

        private static bool HasGameAssemblyDefine()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
            return ContainsDefine(defines, GameAssemblyDefine);
        }

        private static void AddGameAssemblyDefine()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
            if (ContainsDefine(defines, GameAssemblyDefine))
            {
                return;
            }

            var symbols = new List<string>();
            foreach (string symbol in defines.Split(';'))
            {
                if (!string.IsNullOrWhiteSpace(symbol) && !symbols.Contains(symbol))
                {
                    symbols.Add(symbol);
                }
            }

            symbols.Add(GameAssemblyDefine);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                BuildTargetGroup.Standalone,
                string.Join(";", symbols));
        }

        private static bool ContainsDefine(string defines, string expected)
        {
            foreach (string symbol in defines.Split(';'))
            {
                if (string.Equals(symbol.Trim(), expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetInitializationPreferenceKey()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return "EditorTweaks.TemplateBootstrap." + projectRoot;
        }

        private static void ShowError(string message)
        {
            Debug.LogError("ADOFAI Mod Template initialization failed: " + message);
            EditorUtility.DisplayDialog("ADOFAI Mod Template Initialization Failed", message, "OK");
        }
    }
}
