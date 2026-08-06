using System;
using System.IO;
using System.Threading;
using EditorTweaks.Features.ChartRendering;
using EditorTweaks.Features.EditorOverlay;
using EditorTweaks.Patching;
using UnityModManagerNet;

namespace EditorTweaks
{
    /// <summary>
    /// The UnityModManager entry point and the mod lifecycle coordinator.
    /// Runtime assets are loaded before patches are applied and released after
    /// feature services and patches have been shut down.
    /// </summary>
    public static class Main
    {
        public static UnityModManager.ModEntry? Mod { get; private set; }

        public static Settings Settings { get; private set; } = null!;

        internal static int UnityThreadId { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            UnityThreadId = Thread.CurrentThread.ManagedThreadId;
            Mod = modEntry;
            Localization.Load(modEntry);
            Settings = Settings.Load(modEntry);
            Settings.EnsureDefaults(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = Settings.OnGUI;
            modEntry.OnSaveGUI = Settings.OnSaveGUI;

            if (!Settings.HasShownReadme)
            {
                Settings.HasShownReadme = true;
                Settings.Save(modEntry);
                OpenReadme(modEntry);
            }

            modEntry.Logger.Log("EditorTweaks loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (value)
            {
                if (!ResourceLoader.LoadAll(modEntry.Path))
                {
                    modEntry.Logger.Error(
                        "EditorTweaks could not be enabled because one or more AssetBundles failed to load.");
                    return false;
                }

                try
                {
                    PatchManager.ApplyAll(modEntry.Info.Id);

                    if (PatchManager.IsAvailable(PatchFeature.ChartRendering))
                    {
                        ChartRenderService.Ensure();
                    }
                    else
                    {
                        ChartRenderService.Destroy();
                    }

                    if (PatchManager.IsAvailable(PatchFeature.EditorOverlayInputGuard))
                    {
                        EditorTweaksOverlayWindow.Ensure();
                    }
                    else
                    {
                        EditorTweaksOverlayWindow.Destroy();
                    }

                    modEntry.Logger.Log("EditorTweaks enabled.");
                    return true;
                }
                catch (Exception exception)
                {
                    EditorTweaksOverlayWindow.Destroy();
                    ChartRenderService.Destroy();
                    PatchManager.UnpatchAll();
                    ResourceLoader.UnloadAll();
                    modEntry.Logger.Error("EditorTweaks failed to enable: " + exception);
                    return false;
                }
            }

            EditorTweaksOverlayWindow.Destroy();
            ChartRenderService.Destroy();
            PatchManager.UnpatchAll();
            ResourceLoader.UnloadAll();
            modEntry.Logger.Log("EditorTweaks disabled.");
            return true;
        }

        public static void Log(string message)
        {
            Mod?.Logger.Log(message);
        }

        public static void OpenReadme(UnityModManager.ModEntry modEntry)
        {
            string readmePath = Path.Combine(modEntry.Path, "Resources", "README.html");
            if (File.Exists(readmePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = readmePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception exception)
                {
                    modEntry.Logger.Log("Could not open README.html: " + exception.Message);
                }
            }
            else
            {
                modEntry.Logger.Log("README.html not found at: " + readmePath);
            }
        }
    }
}
