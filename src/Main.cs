using System.IO;
using System.Threading;
using ADOFAI.EditorTweaks.Features.ChartRendering;
using ADOFAI.EditorTweaks.Features.EditorOverlay;
using ADOFAI.EditorTweaks.Patching;
using UnityModManagerNet;

namespace ADOFAI.EditorTweaks
{
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

            modEntry.Logger.Log("ADOFAI.EditorTweaks loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (value)
            {
                modEntry.Logger.Log("ADOFAI.EditorTweaks enabled.");
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
            }
            else
            {
                modEntry.Logger.Log("ADOFAI.EditorTweaks disabled.");
                EditorTweaksOverlayWindow.Destroy();
                ChartRenderService.Destroy();
                PatchManager.UnpatchAll();
            }

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
                System.Diagnostics.Process.Start(readmePath);
            }
            else
            {
                modEntry.Logger.Log("README.html not found at: " + readmePath);
            }
        }
    }
}
