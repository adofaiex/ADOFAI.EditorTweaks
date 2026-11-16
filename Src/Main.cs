using System.Reflection;
using System.Threading;
using HarmonyLib;
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
                new Harmony(modEntry.Info.Id).PatchAll(Assembly.GetExecutingAssembly());
                SettingsUi.Enable();
                Coop.CoopSession.Enable();
            }
            else
            {
                modEntry.Logger.Log("ADOFAI.EditorTweaks disabled.");
                new Harmony(modEntry.Info.Id).UnpatchAll(modEntry.Info.Id);
                SettingsUi.Disable();
                Coop.CoopSession.Disable();
            }

            return true;
        }

        public static void Log(string message)
        {
            Mod?.Logger.Log(message);
        }

    }
}
