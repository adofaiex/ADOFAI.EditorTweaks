using ADOFAI.Editor.Preferences.Controls;
using ADOFAI.Editor.Preferences.Models;
using HarmonyLib;

namespace EditorTweaks.Features.EditorPreferences
{
    internal static class EditorPreferencesPersistencePatches
    {
        [HarmonyPatch(typeof(EditorPreferencesEntry), nameof(EditorPreferencesEntry.NotifyChange))]
        private static class EditorPreferencesEntryNotifyChangePatch
        {
            private static void Postfix(EditorPreferencesControl control)
            {
                if (!Main.Settings.PersistEditorPreferences)
                {
                    return;
                }

                try
                {
                    Persistence.generalPrefs.Save();
                }
                catch (System.Exception ex)
                {
                    Main.Log("Failed to persist editor preferences: " + ex);
                }
            }
        }
    }
}
