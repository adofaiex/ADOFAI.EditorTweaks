using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace ADOFAI.EditorTweaks
{
    internal static class Localization
    {
        private const string LocalizationFileName = "localization.json";

        private static readonly Dictionary<string, LocalizationEntry> entries = new Dictionary<string, LocalizationEntry>();

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            entries.Clear();

            string path = Path.Combine(modEntry.Path, "Resources", LocalizationFileName);
            if (!File.Exists(path))
            {
                Main.Log("Localization file not found: " + path);
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (!(GDMiniJSON.Json.Deserialize(json) is Dictionary<string, object> root)
                    || !root.TryGetValue("entries", out object rawEntries)
                    || !(rawEntries is List<object> list))
                {
                    Main.Log("Localization file is empty or malformed: " + path);
                    return;
                }

                foreach (object rawEntry in list)
                {
                    if (!(rawEntry is Dictionary<string, object> item)
                        || !item.TryGetValue("key", out object rawKey)
                        || !(rawKey is string key)
                        || string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    entries[key] = new LocalizationEntry
                    {
                        En = GetString(item, "en"),
                        Zh = GetString(item, "zh")
                    };
                }

                Main.Log("Loaded localization entries: " + entries.Count);
            }
            catch (System.Exception ex)
            {
                Main.Log("Failed to load localization: " + ex);
            }
        }

        public static string Text(string key)
        {
            if (!entries.TryGetValue(key, out LocalizationEntry entry))
            {
                return key;
            }

            string text = IsChinese() ? entry.Zh : entry.En;
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }

            return !string.IsNullOrEmpty(entry.En) ? entry.En : key;
        }

        private static string GetString(Dictionary<string, object> item, string key)
        {
            return item.TryGetValue(key, out object value) && value is string text ? text : string.Empty;
        }

        private static bool IsChinese()
        {
            return RDString.language == SystemLanguage.Chinese
                || RDString.language == SystemLanguage.ChineseSimplified
                || RDString.language == SystemLanguage.ChineseTraditional;
        }

        private sealed class LocalizationEntry
        {
            public string En = string.Empty;

            public string Zh = string.Empty;
        }
    }
}
