using System;
using System.Threading;
using ADOFAI.EditorTweaks.Features.ChartRendering;
using ADOFAI.EditorTweaks.Features.WebUi;
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

                WebUiHost.Ensure();
            }
            else
            {
                modEntry.Logger.Log("ADOFAI.EditorTweaks disabled.");
                WebUiHost.Destroy();
                ChartRenderService.Destroy();
                PatchManager.UnpatchAll();
            }

            return true;
        }

        public static void Log(string message)
        {
            Mod?.Logger.Log(message);
        }

    }
}
