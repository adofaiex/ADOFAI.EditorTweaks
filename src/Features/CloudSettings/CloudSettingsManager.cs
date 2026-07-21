using System;
using System.Collections.Generic;
using System.Text;
using ADOFAI.SteamIntegration;
using GDMiniJSON;
using Steamworks;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.CloudSettings
{
    public static class CloudSettingsManager
    {
        private const string CloudFileName = "ado_fai_editor_tweaks_settings";
        private const string VersionKey = "cloud_version";
        private const string SettingsKey = "settings";

        public static bool IsSteamAvailable => SteamController.initialized;

        public static bool HasCloudFile()
        {
            return SteamController.initialized && SteamRemoteStorage.FileExists(CloudFileName);
        }

        public static bool TryReadFromCloud(Settings settings)
        {
            if (!SteamController.initialized)
            {
                Main.Log("[CloudSettings] Steam not initialized, skipping cloud read.");
                return false;
            }

            if (!SteamRemoteStorage.FileExists(CloudFileName))
            {
                Main.Log("[CloudSettings] No cloud settings file found.");
                return false;
            }

            byte[] data = SteamRemoteStorage.FileRead(CloudFileName);
            if (data == null || data.Length == 0)
            {
                Main.Log("[CloudSettings] Cloud file is empty or could not be read.");
                return false;
            }

            string json = Encoding.UTF8.GetString(data);
            if (string.IsNullOrWhiteSpace(json) || json.Length < 10 || json[0] != '{')
            {
                Main.Log("[CloudSettings] Cloud file content is invalid JSON.");
                return false;
            }

            Dictionary<string, object>? root;
            try
            {
                root = Json.Deserialize(json) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                Main.Log($"[CloudSettings] Failed to deserialize cloud data: {ex.Message}");
                return false;
            }

            if (root == null)
            {
                Main.Log("[CloudSettings] Deserialized cloud root is null.");
                return false;
            }

            string cloudVersion = GetStringValue(root, VersionKey);
            if (string.IsNullOrWhiteSpace(cloudVersion))
            {
                Main.Log("[CloudSettings] Cloud data missing version, skipping.");
                return false;
            }

            Main.Log($"[CloudSettings] Cloud version: {cloudVersion}, local version: {GetModVersion()}");

            if (!root.TryGetValue(SettingsKey, out object settingsObj)
                || !(settingsObj is Dictionary<string, object> settingsDict))
            {
                Main.Log("[CloudSettings] Cloud data missing settings dictionary.");
                return false;
            }

            FromCloudDict(settings, settingsDict);
            Main.Log("[CloudSettings] Cloud settings applied successfully.");
            return true;
        }

        public static bool WriteToCloud(Settings settings)
        {
            if (!SteamController.initialized)
            {
                Main.Log("[CloudSettings] Steam not initialized, skipping cloud write.");
                return false;
            }

            Dictionary<string, object> root = new Dictionary<string, object>
            {
                [VersionKey] = GetModVersion(),
                [SettingsKey] = ToCloudDict(settings)
            };

            string json = Json.Serialize(root);
            if (string.IsNullOrWhiteSpace(json))
            {
                Main.Log("[CloudSettings] Failed to serialize settings to JSON.");
                return false;
            }

            byte[] data = Encoding.UTF8.GetBytes(json);
            bool success = SteamRemoteStorage.FileWrite(CloudFileName, data);
            if (success)
            {
                Main.Log("[CloudSettings] Cloud settings written successfully.");
            }
            else
            {
                Main.Log("[CloudSettings] SteamRemoteStorage.FileWrite failed.");
            }

            return success;
        }

        private static string GetModVersion()
        {
            return Main.Mod?.Info?.Version ?? "0.0.0";
        }

        private static Dictionary<string, object> ToCloudDict(Settings s)
        {
            return new Dictionary<string, object>
            {
                ["EnableNumericDrag"] = s.EnableNumericDrag,
                ["EnableCameraRelativeDecorationDragFix"] = s.EnableCameraRelativeDecorationDragFix,
                ["EnableDecorationPivotFix"] = s.EnableDecorationPivotFix,
                ["EnableVideoBackgroundSyncFix"] = s.EnableVideoBackgroundSyncFix,
                ["PersistEditorPreferences"] = s.PersistEditorPreferences,
                ["ShowEditorOverlay"] = s.ShowEditorOverlay,
                ["EditorOverlayX"] = s.EditorOverlayX,
                ["EditorOverlayY"] = s.EditorOverlayY,
                ["DecorationMoveSnapStep"] = s.DecorationMoveSnapStep,
                ["FloatStepPerPixel"] = s.FloatStepPerPixel,
                ["IntStepPerPixel"] = s.IntStepPerPixel,
                ["MaxFloatingPoints"] = s.MaxFloatingPoints,
                ["ChartRenderWorkspaceDirectory"] = s.ChartRenderWorkspaceDirectory ?? string.Empty,
                ["ChartRenderExportDirectory"] = s.ChartRenderExportDirectory ?? string.Empty,
                ["ChartRenderWidth"] = s.ChartRenderWidth,
                ["ChartRenderHeight"] = s.ChartRenderHeight,
                ["ChartRenderFps"] = s.ChartRenderFps,
                ["ChartRenderCrf"] = s.ChartRenderCrf,
                ["ChartRenderBitrateMbps"] = s.ChartRenderBitrateMbps,
                ["ChartRenderPreset"] = s.ChartRenderPreset ?? string.Empty,
                ["ChartRenderEncoderMode"] = s.ChartRenderEncoderMode ?? string.Empty,
                ["ChartRenderCaptureFormat"] = s.ChartRenderCaptureFormat ?? string.Empty,
                ["ChartRenderPreviewMode"] = s.ChartRenderPreviewMode ?? string.Empty,
                ["ChartRenderAudioFormat"] = s.ChartRenderAudioFormat ?? string.Empty,
                ["ChartRenderVideoFormat"] = s.ChartRenderVideoFormat ?? string.Empty,
                ["ChartRenderCompletionTailSeconds"] = s.ChartRenderCompletionTailSeconds,
                ["ChartRenderAudioSyncOffsetMs"] = s.ChartRenderAudioSyncOffsetMs,
                ["ChartRenderShowHitJudgments"] = s.ChartRenderShowHitJudgments,
                ["ChartRenderUseSelectedRange"] = s.ChartRenderUseSelectedRange,
                ["ChartRenderCustomMuxArgs"] = s.ChartRenderCustomMuxArgs ?? string.Empty
            };
        }

        private static void FromCloudDict(Settings s, Dictionary<string, object> d)
        {
            s.EnableNumericDrag = GetBoolValue(d, "EnableNumericDrag", true);
            s.EnableCameraRelativeDecorationDragFix = GetBoolValue(d, "EnableCameraRelativeDecorationDragFix", true);
            s.EnableDecorationPivotFix = GetBoolValue(d, "EnableDecorationPivotFix", true);
            s.EnableVideoBackgroundSyncFix = GetBoolValue(d, "EnableVideoBackgroundSyncFix", true);
            s.PersistEditorPreferences = GetBoolValue(d, "PersistEditorPreferences", true);
            s.ShowEditorOverlay = GetBoolValue(d, "ShowEditorOverlay", true);
            s.EditorOverlayX = GetFloatValue(d, "EditorOverlayX", -1f);
            s.EditorOverlayY = GetFloatValue(d, "EditorOverlayY", -1f);
            s.DecorationMoveSnapStep = GetFloatValue(d, "DecorationMoveSnapStep", 0.5f);
            s.FloatStepPerPixel = GetFloatValue(d, "FloatStepPerPixel", 0.1f);
            s.IntStepPerPixel = GetFloatValue(d, "IntStepPerPixel", 1f);
            s.MaxFloatingPoints = GetIntValue(d, "MaxFloatingPoints", 3);
            s.ChartRenderWorkspaceDirectory = GetStringValue(d, "ChartRenderWorkspaceDirectory");
            s.ChartRenderExportDirectory = GetStringValue(d, "ChartRenderExportDirectory");
            s.ChartRenderWidth = GetIntValue(d, "ChartRenderWidth", 1920);
            s.ChartRenderHeight = GetIntValue(d, "ChartRenderHeight", 1080);
            s.ChartRenderFps = GetIntValue(d, "ChartRenderFps", 60);
            s.ChartRenderCrf = GetIntValue(d, "ChartRenderCrf", 18);
            s.ChartRenderBitrateMbps = GetIntValue(d, "ChartRenderBitrateMbps", 0);
            s.ChartRenderPreset = GetStringValue(d, "ChartRenderPreset") ?? "veryfast";
            s.ChartRenderEncoderMode = GetStringValue(d, "ChartRenderEncoderMode") ?? string.Empty;
            s.ChartRenderCaptureFormat = GetStringValue(d, "ChartRenderCaptureFormat") ?? string.Empty;
            s.ChartRenderPreviewMode = GetStringValue(d, "ChartRenderPreviewMode") ?? string.Empty;
            s.ChartRenderAudioFormat = GetStringValue(d, "ChartRenderAudioFormat") ?? string.Empty;
            s.ChartRenderVideoFormat = GetStringValue(d, "ChartRenderVideoFormat") ?? string.Empty;
            s.ChartRenderCompletionTailSeconds = GetFloatValue(d, "ChartRenderCompletionTailSeconds", 5f);
            s.ChartRenderAudioSyncOffsetMs = GetFloatValue(d, "ChartRenderAudioSyncOffsetMs", 0f);
            s.ChartRenderShowHitJudgments = GetBoolValue(d, "ChartRenderShowHitJudgments", true);
            s.ChartRenderUseSelectedRange = GetBoolValue(d, "ChartRenderUseSelectedRange", false);
            s.ChartRenderCustomMuxArgs = GetStringValue(d, "ChartRenderCustomMuxArgs") ?? string.Empty;
        }

        private static bool GetBoolValue(Dictionary<string, object> d, string key, bool fallback)
        {
            if (!d.TryGetValue(key, out object value))
            {
                return fallback;
            }

            if (value is bool b)
            {
                return b;
            }

            return fallback;
        }

        private static int GetIntValue(Dictionary<string, object> d, string key, int fallback)
        {
            if (!d.TryGetValue(key, out object value))
            {
                return fallback;
            }

            if (value is int i)
            {
                return i;
            }

            if (value is long l)
            {
                return (int)l;
            }

            if (value is double f)
            {
                return (int)f;
            }

            return fallback;
        }

        private static float GetFloatValue(Dictionary<string, object> d, string key, float fallback)
        {
            if (!d.TryGetValue(key, out object value))
            {
                return fallback;
            }

            if (value is float f)
            {
                return f;
            }

            if (value is double dbl)
            {
                return (float)dbl;
            }

            if (value is long l)
            {
                return l;
            }

            if (value is int i)
            {
                return i;
            }

            return fallback;
        }

        private static string GetStringValue(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out object value))
            {
                return string.Empty;
            }

            return value as string ?? string.Empty;
        }
    }
}
