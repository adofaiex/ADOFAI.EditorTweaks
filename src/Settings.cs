using System;
using System.IO;
using ADOFAI.EditorTweaks.Features.ArchiveIo;
using ADOFAI.EditorTweaks.Features.ChartRendering;
using ADOFAI.EditorTweaks.Features.WebUi;
using UnityModManagerNet;
using UnityEngine;

namespace ADOFAI.EditorTweaks
{
    public class Settings : UnityModManager.ModSettings
    {
        private const int MinChartRenderSize = 16;
        private const int MaxChartRenderWidth = 7680;
        private const int MaxChartRenderHeight = 4320;
        private const int MinChartRenderFps = 1;
        private const int MaxChartRenderFps = 240;
        private const int MinChartRenderCrf = 0;
        private const int MaxChartRenderCrf = 51;
        private const float MinChartRenderAudioSyncOffsetMs = -5000f;
        private const float MaxChartRenderAudioSyncOffsetMs = 5000f;

        public bool EnableNumericDrag = true;

        public bool EnableCameraRelativeDecorationDragFix = true;

        public bool EnableDecorationPivotFix = true;

        public bool EnableVideoBackgroundSyncFix = true;

        public bool PersistEditorPreferences = true;

        public string WebUiOpenHotkey = WebUiHotkey.Default;

        // These fields remain readable so older settings files and cloud files can still deserialize.
        // They are intentionally not used by the new UI or runtime.
        public bool ShowEditorOverlay = true;

        public bool EditorOverlayCollapsed = false;

        public float EditorOverlayX = -1f;

        public float EditorOverlayY = -1f;

        public string LegacyZipEncoding = LegacyZipEncodingModes.Auto;

        public float DecorationMoveSnapStep = 0.5f;

        public float FloatStepPerPixel = 0.1f;

        public float IntStepPerPixel = 1f;

        public int MaxFloatingPoints = 3;

        public string ChartRenderWorkspaceDirectory = string.Empty;

        public string ChartRenderExportDirectory = string.Empty;

        public int ChartRenderWidth = 1920;

        public int ChartRenderHeight = 1080;

        public int ChartRenderFps = 60;

        public int ChartRenderCrf = 18;

        public int ChartRenderBitrateMbps = ChartRenderBitratePresets.AutoBitrateMbps;

        public string ChartRenderPreset = "veryfast";

        public string ChartRenderEncoderMode = ChartRenderOptionValues.EncoderAutoBalanced;

        public string ChartRenderCaptureFormat = ChartRenderOptionValues.CaptureRgba;

        public string ChartRenderCaptureSource = ChartRenderOptionValues.CaptureSourceCamera;

        public string ChartRenderPreviewMode = ChartRenderOptionValues.PreviewFull;

        public string ChartRenderAudioFormat = ChartRenderOptionValues.AudioFormatAac;

        public string ChartRenderVideoFormat = ChartRenderOptionValues.VideoFormatMp4;

        public float ChartRenderCompletionTailSeconds = 5f;

        public float ChartRenderAudioSyncOffsetMs;

        public bool ChartRenderShowHitJudgments = true;

        public bool ChartRenderUseSelectedRange;

        public bool ChartRenderAdvancedSettingsExpanded;

        public bool ChartRenderProfessionalSettingsExpanded;

        public string ChartRenderCustomMuxArgs = string.Empty;

        public bool HasShownReadme;

        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            WebUiSettingsView.Draw(this);
        }

        public void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Save(modEntry);
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Normalize();
            Save(this, modEntry);
        }

        public void EnsureDefaults(UnityModManager.ModEntry modEntry)
        {
            if (string.IsNullOrWhiteSpace(ChartRenderWorkspaceDirectory))
            {
                ChartRenderWorkspaceDirectory = GetDefaultWorkspaceDirectory(modEntry);
            }

            if (string.IsNullOrWhiteSpace(ChartRenderExportDirectory))
            {
                ChartRenderExportDirectory = GetDefaultExportDirectory(modEntry);
            }

            Normalize();
        }

        public void Normalize()
        {
            WebUiOpenHotkey = WebUiHotkey.NormalizeOrDefault(WebUiOpenHotkey);
            LegacyZipEncoding = LegacyZipEncodingModes.Normalize(LegacyZipEncoding);
            DecorationMoveSnapStep = Mathf.Max(0f, DecorationMoveSnapStep);
            FloatStepPerPixel = Mathf.Max(0.0001f, FloatStepPerPixel);
            IntStepPerPixel = Mathf.Max(0.0001f, IntStepPerPixel);
            MaxFloatingPoints = Mathf.Clamp(MaxFloatingPoints, 0, 8);

            ChartRenderWidth = MakeEven(Mathf.Clamp(ChartRenderWidth, MinChartRenderSize, MaxChartRenderWidth));
            ChartRenderHeight = MakeEven(Mathf.Clamp(ChartRenderHeight, MinChartRenderSize, MaxChartRenderHeight));
            ChartRenderFps = Mathf.Clamp(ChartRenderFps, MinChartRenderFps, MaxChartRenderFps);
            ChartRenderCrf = Mathf.Clamp(ChartRenderCrf, MinChartRenderCrf, MaxChartRenderCrf);
            ChartRenderBitrateMbps = Mathf.Clamp(ChartRenderBitrateMbps, ChartRenderBitratePresets.AutoBitrateMbps, ChartRenderBitratePresets.MaxBitrateMbps);
            ChartRenderPreset = string.IsNullOrWhiteSpace(ChartRenderPreset) ? "veryfast" : ChartRenderPreset.Trim();
            ChartRenderEncoderMode = ChartRenderOptionValues.NormalizeEncoderMode(ChartRenderEncoderMode);
            ChartRenderCaptureFormat = ChartRenderOptionValues.NormalizeCaptureFormat(ChartRenderCaptureFormat);
            ChartRenderCaptureSource = ChartRenderOptionValues.NormalizeCaptureSource(ChartRenderCaptureSource);
            ChartRenderPreviewMode = ChartRenderOptionValues.NormalizePreviewMode(ChartRenderPreviewMode);
            ChartRenderAudioFormat = ChartRenderOptionValues.NormalizeAudioFormat(ChartRenderAudioFormat);
            ChartRenderVideoFormat = ChartRenderOptionValues.NormalizeVideoFormat(ChartRenderVideoFormat);
            ChartRenderCompletionTailSeconds = Mathf.Max(0f, ChartRenderCompletionTailSeconds);
            ChartRenderAudioSyncOffsetMs = Mathf.Clamp(ChartRenderAudioSyncOffsetMs, MinChartRenderAudioSyncOffsetMs, MaxChartRenderAudioSyncOffsetMs);
            ChartRenderWorkspaceDirectory = ChartRenderWorkspaceDirectory ?? string.Empty;
            ChartRenderExportDirectory = ChartRenderExportDirectory ?? string.Empty;
            ChartRenderCustomMuxArgs = ChartRenderCustomMuxArgs ?? string.Empty;
        }

        public void ResetAllDefaults(UnityModManager.ModEntry modEntry)
        {
            EnableNumericDrag = true;
            EnableCameraRelativeDecorationDragFix = true;
            EnableDecorationPivotFix = true;
            EnableVideoBackgroundSyncFix = true;
            PersistEditorPreferences = true;
            WebUiOpenHotkey = WebUiHotkey.Default;
            LegacyZipEncoding = LegacyZipEncodingModes.Auto;
            DecorationMoveSnapStep = 0.5f;
            FloatStepPerPixel = 0.1f;
            IntStepPerPixel = 1f;
            MaxFloatingPoints = 3;

            ChartRenderWorkspaceDirectory = GetDefaultWorkspaceDirectory(modEntry);
            ChartRenderExportDirectory = GetDefaultExportDirectory(modEntry);
            ChartRenderWidth = 1920;
            ChartRenderHeight = 1080;
            ChartRenderFps = 60;
            ChartRenderCrf = 18;
            ChartRenderBitrateMbps = ChartRenderBitratePresets.AutoBitrateMbps;
            ChartRenderPreset = "veryfast";
            ChartRenderEncoderMode = ChartRenderOptionValues.EncoderAutoBalanced;
            ChartRenderCaptureFormat = ChartRenderOptionValues.CaptureRgba;
            ChartRenderCaptureSource = ChartRenderOptionValues.CaptureSourceCamera;
            ChartRenderPreviewMode = ChartRenderOptionValues.PreviewFull;
            ChartRenderAudioFormat = ChartRenderOptionValues.AudioFormatAac;
            ChartRenderVideoFormat = ChartRenderOptionValues.VideoFormatMp4;
            ChartRenderCompletionTailSeconds = 5f;
            ChartRenderAudioSyncOffsetMs = 0f;
            ChartRenderShowHitJudgments = true;
            ChartRenderUseSelectedRange = false;
            ChartRenderAdvancedSettingsExpanded = false;
            ChartRenderProfessionalSettingsExpanded = false;
            ChartRenderCustomMuxArgs = string.Empty;
            Normalize();
        }

        public static string Text(string key)
        {
            return Localization.Text(key);
        }

        public static Settings Load(UnityModManager.ModEntry modEntry)
        {
            return Load<Settings>(modEntry);
        }

        private static int MakeEven(int value)
        {
            return value % 2 == 0 ? value : value + 1;
        }

        private static string GetDefaultWorkspaceDirectory(UnityModManager.ModEntry modEntry)
        {
            return Path.Combine(modEntry.Path, "Workspace");
        }

        private static string GetDefaultExportDirectory(UnityModManager.ModEntry modEntry)
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            return string.IsNullOrWhiteSpace(videos)
                ? Path.Combine(GetDefaultWorkspaceDirectory(modEntry), "Exports")
                : Path.Combine(videos, "ADOFAI Renders");
        }
    }
}
