using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ADOFAI.EditorTweaks.Features.ChartRendering;
using ADOFAI.EditorTweaks.Features.CloudSettings;
using ADOFAI.EditorTweaks.Patching;
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
        private const int DefaultChartRenderWidth = 1920;
        private const int DefaultChartRenderHeight = 1080;
        private const int DefaultChartRenderFps = 60;
        private const int DefaultChartRenderCrf = 18;
        private const int DefaultChartRenderBitrateMbps = ChartRenderBitratePresets.AutoBitrateMbps;
        private const float DefaultChartRenderCompletionTailSeconds = 5f;
        private const float DefaultChartRenderAudioSyncOffsetMs = 0f;
        private const float MinChartRenderAudioSyncOffsetMs = -5000f;
        private const float MaxChartRenderAudioSyncOffsetMs = 5000f;
        private const bool DefaultChartRenderShowHitJudgments = true;
        private const bool DefaultChartRenderUseSelectedRange = false;
        private const bool DefaultChartRenderAdvancedSettingsExpanded = false;
        private const string DefaultChartRenderPreset = "veryfast";
        private const string DefaultChartRenderEncoderMode = ChartRenderOptionValues.EncoderAutoBalanced;
        private const string DefaultChartRenderCaptureFormat = ChartRenderOptionValues.CaptureRgba;
        private const string DefaultChartRenderAudioFormat = ChartRenderOptionValues.AudioFormatAac;
        private const string DefaultChartRenderVideoFormat = ChartRenderOptionValues.VideoFormatMp4;
        private const string DefaultChartRenderPreviewMode = ChartRenderOptionValues.PreviewFull;

        public bool EnableNumericDrag = true;

        public bool EnableCameraRelativeDecorationDragFix = true;

        public bool EnableDecorationPivotFix = true;

        public bool EnableVideoBackgroundSyncFix = true;

        public bool PersistEditorPreferences = true;

        public bool ShowEditorOverlay = true;

        public bool EditorOverlayCollapsed = false;

        public float EditorOverlayX = -1f;

        public float EditorOverlayY = -1f;

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

        public int ChartRenderBitrateMbps = DefaultChartRenderBitrateMbps;

        public string ChartRenderPreset = "veryfast";

        public string ChartRenderEncoderMode = DefaultChartRenderEncoderMode;

        public string ChartRenderCaptureFormat = DefaultChartRenderCaptureFormat;

        public string ChartRenderPreviewMode = DefaultChartRenderPreviewMode;

        public string ChartRenderAudioFormat = DefaultChartRenderAudioFormat;

        public string ChartRenderVideoFormat = DefaultChartRenderVideoFormat;

        public float ChartRenderCompletionTailSeconds = 5f;

        public float ChartRenderAudioSyncOffsetMs = 0f;

        public bool ChartRenderShowHitJudgments = true;

        public bool ChartRenderUseSelectedRange = DefaultChartRenderUseSelectedRange;

        public bool ChartRenderAdvancedSettingsExpanded = false;

        public bool ChartRenderProfessionalSettingsExpanded = false;

        public string ChartRenderCustomMuxArgs = string.Empty;

        public bool HasShownReadme = false;

        private static GUIStyle? panelStyle;

        private static GUIStyle? titleStyle;

        private static GUIStyle? sectionStyle;

        private static GUIStyle? labelStyle;

        private static GUIStyle? hintStyle;

        private static GUIStyle? toggleStyle;

        private static GUIStyle? textFieldStyle;

        private string snapStepText = string.Empty;

        private string floatStepText = string.Empty;

        private string intStepText = string.Empty;

        private string decimalsText = string.Empty;

        private string renderWidthText = string.Empty;

        private string renderHeightText = string.Empty;

        private string renderFpsText = string.Empty;

        private string renderCrfText = string.Empty;

        private string renderBitrateText = string.Empty;

        private string renderPresetText = string.Empty;

        private string renderTailSecondsText = string.Empty;

        private string renderAudioSyncOffsetText = string.Empty;

        private string renderCustomMuxArgsText = string.Empty;

        private bool textFieldsInitialized;

        private string cloudStatusMessage = string.Empty;
        private bool cloudStatusIsError;

        private static GUIStyle? cloudButtonStyle;
        private static GUIStyle? cloudSectionBoxStyle;
        private static GUIStyle? cloudTitleStyle;
        private static GUIStyle? cloudStatusStyle;

        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            EnsureStyles();
            EnsureTextFields();

            int oldRenderWidth = ChartRenderWidth;
            int oldRenderHeight = ChartRenderHeight;
            int oldRenderFps = ChartRenderFps;
            int oldRenderCrf = ChartRenderCrf;
            int oldRenderBitrate = ChartRenderBitrateMbps;
            string oldRenderPreset = ChartRenderPreset;
            string oldRenderEncoderMode = ChartRenderEncoderMode;
            string oldRenderCaptureFormat = ChartRenderCaptureFormat;
            string oldRenderPreviewMode = ChartRenderPreviewMode;
            string oldRenderAudioFormat = ChartRenderAudioFormat;
            string oldRenderVideoFormat = ChartRenderVideoFormat;
            float oldRenderTail = ChartRenderCompletionTailSeconds;
            float oldRenderAudioSyncOffset = ChartRenderAudioSyncOffsetMs;
            bool oldRenderJudgments = ChartRenderShowHitJudgments;
            bool oldRenderUseSelectedRange = ChartRenderUseSelectedRange;
            bool oldAdvancedSettingsExpanded = ChartRenderAdvancedSettingsExpanded;
            bool oldProfessionalSettingsExpanded = ChartRenderProfessionalSettingsExpanded;
            string oldRenderCustomMuxArgs = ChartRenderCustomMuxArgs;
            string oldWorkspaceDirectory = ChartRenderWorkspaceDirectory;
            string oldExportDirectory = ChartRenderExportDirectory;

            GUILayout.BeginVertical(panelStyle);
            if (GUILayout.Button(Text("openReadme"), GUILayout.Width(160)))
            {
                Main.OpenReadme(modEntry);
            }

            GUILayout.Space(4);
            GUILayout.Label(Text("title"), titleStyle);
            GUILayout.Space(4);

            DrawPatchCompatibilityStatus();
            DrawCloudSyncSection(modEntry);

            DrawSection(Text("fixesSection"));
            EnableCameraRelativeDecorationDragFix = DrawToggleRow(EnableCameraRelativeDecorationDragFix, Text("fixCameraRelativeDecorationDrag"));
            EnableDecorationPivotFix = DrawToggleRow(EnableDecorationPivotFix, Text("fixDecorationPivot"));
            EnableVideoBackgroundSyncFix = DrawToggleRow(EnableVideoBackgroundSyncFix, Text("fixVideoBackgroundSync"));
            PersistEditorPreferences = DrawToggleRow(PersistEditorPreferences, Text("persistEditorPreferences"));

            DrawSection(Text("overlaySection"));
            ShowEditorOverlay = DrawToggleRow(ShowEditorOverlay, Text("showEditorOverlay"));

            DrawSection(Text("numericSection"));
            EnableNumericDrag = DrawToggleRow(EnableNumericDrag, Text("enableNumericDrag"));
            FloatStepPerPixel = DrawFloatRow(Text("floatStepPerPixel"), FloatStepPerPixel, ref floatStepText, 0.0001f);
            IntStepPerPixel = DrawFloatRow(Text("intStepPerPixel"), IntStepPerPixel, ref intStepText, 0.0001f);
            MaxFloatingPoints = DrawIntRow(Text("maxFloatDecimals"), MaxFloatingPoints, ref decimalsText, 0, 8);

            DrawSection(Text("decorationSection"));
            DecorationMoveSnapStep = DrawFloatRow(Text("decorationMoveSnapStep"), DecorationMoveSnapStep, ref snapStepText, 0f, Text("zeroDisables"));

            DrawSection(Text("renderSection"));
            GUILayout.Label(Text("chartRenderBasicHint"), hintStyle);
            ChartRenderExportDirectory = DrawTextSettingRow(Text("chartRenderExportDirectory"), Text("chartRenderExportDirectoryHint"), ChartRenderExportDirectory, GetDefaultExportDirectory(modEntry));
            DrawResolutionPresetRow();
            ChartRenderWidth = DrawIntSettingRow(Text("chartRenderWidth"), Text("chartRenderWidthHint"), ChartRenderWidth, ref renderWidthText, MinChartRenderSize, MaxChartRenderWidth, DefaultChartRenderWidth);
            ChartRenderHeight = DrawIntSettingRow(Text("chartRenderHeight"), Text("chartRenderHeightHint"), ChartRenderHeight, ref renderHeightText, MinChartRenderSize, MaxChartRenderHeight, DefaultChartRenderHeight);
            DrawFpsPresetRow();
            ChartRenderFps = DrawIntSettingRow(Text("chartRenderFps"), Text("chartRenderFpsHint"), ChartRenderFps, ref renderFpsText, MinChartRenderFps, MaxChartRenderFps, DefaultChartRenderFps);
            ChartRenderCompletionTailSeconds = DrawFloatSettingRow(Text("chartRenderCompletionTailSeconds"), Text("chartRenderCompletionTailSecondsHint"), ChartRenderCompletionTailSeconds, ref renderTailSecondsText, 0f, DefaultChartRenderCompletionTailSeconds);
            ChartRenderVideoFormat = DrawChoiceSettingRow(Text("chartRenderVideoFormat"), Text("chartRenderVideoFormatHint"), ChartRenderVideoFormat, ChartRenderOptionValues.VideoFormats, GetVideoFormatLabels(), DefaultChartRenderVideoFormat);
            ChartRenderShowHitJudgments = DrawToggleSettingRow(Text("chartRenderShowHitJudgments"), Text("chartRenderShowHitJudgmentsHint"), ChartRenderShowHitJudgments, DefaultChartRenderShowHitJudgments);
            ChartRenderUseSelectedRange = DrawToggleSettingRow(Text("chartRenderUseSelectedRange"), Text("chartRenderUseSelectedRangeHint"), ChartRenderUseSelectedRange, DefaultChartRenderUseSelectedRange);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Text("chartRenderResetAll"), GUILayout.Width(150)))
            {
                ResetChartRenderDefaults(modEntry);
            }

            if (GUILayout.Button(ChartRenderAdvancedSettingsExpanded ? Text("chartRenderHideAdvanced") : Text("chartRenderShowAdvanced"), GUILayout.Width(170)))
            {
                ChartRenderAdvancedSettingsExpanded = !ChartRenderAdvancedSettingsExpanded;
            }

            if (GUILayout.Button(ChartRenderProfessionalSettingsExpanded ? Text("chartRenderHideProfessional") : Text("chartRenderShowProfessional"), GUILayout.Width(170)))
            {
                ChartRenderProfessionalSettingsExpanded = !ChartRenderProfessionalSettingsExpanded;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (ChartRenderAdvancedSettingsExpanded)
            {
                GUILayout.Label(Text("chartRenderAdvancedWarning"), hintStyle);
                GUILayout.Label(Text("chartRenderAdvancedHint"), hintStyle);

                ChartRenderWorkspaceDirectory = DrawTextSettingRow(Text("chartRenderWorkspaceDirectory"), Text("chartRenderWorkspaceDirectoryHint"), ChartRenderWorkspaceDirectory, GetDefaultWorkspaceDirectory(modEntry));
                ChartRenderEncoderMode = DrawChoiceSettingRow(Text("chartRenderEncoderMode"), Text("chartRenderEncoderModeHint"), ChartRenderEncoderMode, ChartRenderOptionValues.EncoderModes, GetEncoderModeLabels(), DefaultChartRenderEncoderMode);
                ChartRenderCrf = DrawIntSettingRow(Text("chartRenderCrf"), Text("chartRenderCrfHint"), ChartRenderCrf, ref renderCrfText, MinChartRenderCrf, MaxChartRenderCrf, DefaultChartRenderCrf);
                ChartRenderBitrateMbps = DrawIntSettingRow(Text("chartRenderBitrateMbps"), GetBitrateHint(), ChartRenderBitrateMbps, ref renderBitrateText, ChartRenderBitratePresets.AutoBitrateMbps, ChartRenderBitratePresets.MaxBitrateMbps, DefaultChartRenderBitrateMbps);
                if (ChartRenderOptionValues.NormalizeEncoderMode(ChartRenderEncoderMode) == ChartRenderOptionValues.EncoderCustom)
                {
                    ChartRenderPreset = DrawStringSettingRow(Text("chartRenderPreset"), Text("chartRenderPresetHint"), ChartRenderPreset, ref renderPresetText, DefaultChartRenderPreset);
                }

                ChartRenderCaptureFormat = DrawChoiceSettingRow(Text("chartRenderCaptureFormat"), Text("chartRenderCaptureFormatHint"), ChartRenderCaptureFormat, ChartRenderOptionValues.CaptureFormats, GetCaptureFormatLabels(), DefaultChartRenderCaptureFormat);
                ChartRenderAudioFormat = DrawChoiceSettingRow(Text("chartRenderAudioFormat"), Text("chartRenderAudioFormatHint"), ChartRenderAudioFormat, ChartRenderOptionValues.AudioFormats, GetAudioFormatLabels(), DefaultChartRenderAudioFormat);
                ChartRenderPreviewMode = DrawChoiceSettingRow(Text("chartRenderPreviewMode"), Text("chartRenderPreviewModeHint"), ChartRenderPreviewMode, ChartRenderOptionValues.PreviewModes, GetPreviewModeLabels(), DefaultChartRenderPreviewMode);
                ChartRenderAudioSyncOffsetMs = DrawFloatSettingRow(Text("chartRenderAudioSyncOffsetMs"), Text("chartRenderAudioSyncOffsetMsHint"), ChartRenderAudioSyncOffsetMs, ref renderAudioSyncOffsetText, MinChartRenderAudioSyncOffsetMs, DefaultChartRenderAudioSyncOffsetMs);
            }

            if (ChartRenderProfessionalSettingsExpanded)
            {
                GUILayout.Label(Text("chartRenderProfessionalHint"), hintStyle);
                if (GUILayout.Button(Text("chartRenderHelpButton"), GUILayout.Width(200)))
                {
                    OpenHelpFile(modEntry);
                }

                ChartRenderCustomMuxArgs = DrawStringSettingRow(Text("chartRenderCustomMuxArgs"), Text("chartRenderCustomMuxArgsHint"), ChartRenderCustomMuxArgs, ref renderCustomMuxArgsText, string.Empty);
            }

            GUILayout.Space(2);
            GUILayout.EndVertical();

            NormalizeChartRenderSettings();
            if (oldRenderWidth != ChartRenderWidth
                || oldRenderHeight != ChartRenderHeight
                || oldRenderFps != ChartRenderFps
                || oldRenderCrf != ChartRenderCrf
                || oldRenderBitrate != ChartRenderBitrateMbps
                || oldRenderPreset != ChartRenderPreset
                || oldRenderEncoderMode != ChartRenderEncoderMode
                || oldRenderCaptureFormat != ChartRenderCaptureFormat
                || oldRenderPreviewMode != ChartRenderPreviewMode
                || oldRenderAudioFormat != ChartRenderAudioFormat
                || oldRenderVideoFormat != ChartRenderVideoFormat
                || oldRenderTail != ChartRenderCompletionTailSeconds
                || oldRenderAudioSyncOffset != ChartRenderAudioSyncOffsetMs
                || oldRenderJudgments != ChartRenderShowHitJudgments
                || oldRenderUseSelectedRange != ChartRenderUseSelectedRange
                || oldAdvancedSettingsExpanded != ChartRenderAdvancedSettingsExpanded
                || oldProfessionalSettingsExpanded != ChartRenderProfessionalSettingsExpanded
                || oldRenderCustomMuxArgs != ChartRenderCustomMuxArgs
                || oldWorkspaceDirectory != ChartRenderWorkspaceDirectory
                || oldExportDirectory != ChartRenderExportDirectory)
            {
                Save(modEntry);
            }
        }

        private void EnsureTextFields()
        {
            if (textFieldsInitialized)
            {
                return;
            }

            snapStepText = FormatFloat(DecorationMoveSnapStep);
            floatStepText = FormatFloat(FloatStepPerPixel);
            intStepText = FormatFloat(IntStepPerPixel);
            decimalsText = MaxFloatingPoints.ToString(CultureInfo.InvariantCulture);
            renderWidthText = ChartRenderWidth.ToString(CultureInfo.InvariantCulture);
            renderHeightText = ChartRenderHeight.ToString(CultureInfo.InvariantCulture);
            renderFpsText = ChartRenderFps.ToString(CultureInfo.InvariantCulture);
            renderCrfText = ChartRenderCrf.ToString(CultureInfo.InvariantCulture);
            renderBitrateText = ChartRenderBitrateMbps.ToString(CultureInfo.InvariantCulture);
            renderPresetText = ChartRenderPreset;
            renderTailSecondsText = FormatFloat(ChartRenderCompletionTailSeconds);
            renderAudioSyncOffsetText = FormatFloat(ChartRenderAudioSyncOffsetMs);
            renderCustomMuxArgsText = ChartRenderCustomMuxArgs;
            textFieldsInitialized = true;
        }

        private static void EnsureStyles()
        {
            if (panelStyle != null)
            {
                return;
            }

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(14, 14, 12, 14),
                margin = new RectOffset(4, 4, 4, 4)
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.92f, 0.98f, 1f, 1f) }
            };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 5, 2),
                normal = { textColor = new Color(0.74f, 0.88f, 1f, 1f) }
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 8, 0, 0)
            };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.72f, 0.72f, 0.72f, 1f) }
            };
            toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 3, 3),
                padding = new RectOffset(24, 6, 3, 3)
            };
            textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 22
            };

            cloudButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 34,
                margin = new RectOffset(4, 4, 4, 4),
                padding = new RectOffset(14, 14, 6, 6)
            };
            cloudSectionBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(0, 0, 4, 12)
            };
            cloudTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.82f, 0.92f, 1f, 1f) }
            };
            cloudStatusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 3, 0)
            };
        }

        private static void DrawSection(string text)
        {
            GUILayout.Space(8);
            GUILayout.Label(text, sectionStyle);
            Rect rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorTweaksGui.DrawRect(rect, new Color(0.35f, 0.52f, 0.70f, 0.45f));
            GUILayout.Space(2);
        }

        private static bool DrawToggleRow(bool value, string text)
        {
            GUILayout.BeginHorizontal();
            bool next = GUILayout.Toggle(value, text, toggleStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            return next;
        }

        private static float DrawFloatRow(string label, float value, ref string text, float min, string? hint = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(190));
            text = GUILayout.TextField(text, textFieldStyle, GUILayout.Width(96));
            if (TryParseFloat(text, out float next))
            {
                value = Mathf.Max(min, next);
            }

            if (!string.IsNullOrEmpty(hint))
            {
                GUILayout.Label(hint, hintStyle, GUILayout.Width(90));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        private static int DrawIntRow(string label, int value, ref string text, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(190));
            text = GUILayout.TextField(text, textFieldStyle, GUILayout.Width(96));
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int next))
            {
                value = Mathf.Clamp(next, min, max);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        private static string DrawTextRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(190));
            string next = GUILayout.TextField(value ?? string.Empty, textFieldStyle, GUILayout.MinWidth(220), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            return next;
        }

        private static int DrawIntSettingRow(string label, string hint, int value, ref string text, int min, int max, int defaultValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(190));
            text = GUILayout.TextField(text, textFieldStyle, GUILayout.Width(96));
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int next))
            {
                value = Mathf.Clamp(next, min, max);
            }

            if (GUILayout.Button(Text("chartRenderReset"), GUILayout.Width(64)))
            {
                value = defaultValue;
                text = value.ToString(CultureInfo.InvariantCulture);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(hint, hintStyle);
            return value;
        }

        private static float DrawFloatSettingRow(string label, string hint, float value, ref string text, float min, float defaultValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(190));
            text = GUILayout.TextField(text, textFieldStyle, GUILayout.Width(96));
            if (TryParseFloat(text, out float next))
            {
                value = Mathf.Max(min, next);
            }

            if (GUILayout.Button(Text("chartRenderReset"), GUILayout.Width(64)))
            {
                value = defaultValue;
                text = FormatFloat(value);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(hint, hintStyle);
            return value;
        }

        private static string DrawTextSettingRow(string label, string hint, string value, string defaultValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(190));
            string next = GUILayout.TextField(value ?? string.Empty, textFieldStyle, GUILayout.MinWidth(220), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(Text("chartRenderReset"), GUILayout.Width(64)))
            {
                next = defaultValue;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(hint, hintStyle);
            return next;
        }

        private static string DrawStringSettingRow(string label, string hint, string value, ref string text, string defaultValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(190));
            text = GUILayout.TextField(text ?? string.Empty, textFieldStyle, GUILayout.Width(140));
            if (!string.IsNullOrWhiteSpace(text))
            {
                value = text.Trim();
            }

            if (GUILayout.Button(Text("chartRenderReset"), GUILayout.Width(64)))
            {
                value = defaultValue;
                text = defaultValue;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(hint, hintStyle);
            return value;
        }

        private void DrawResolutionPresetRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Text("chartRenderResolutionPreset"), labelStyle, GUILayout.Width(190));
            if (GUILayout.Button("1080p", GUILayout.Width(82)))
            {
                ApplyChartRenderResolutionPreset(1920, 1080);
            }

            if (GUILayout.Button("2K", GUILayout.Width(82)))
            {
                ApplyChartRenderResolutionPreset(2560, 1440);
            }

            if (GUILayout.Button("4K", GUILayout.Width(82)))
            {
                ApplyChartRenderResolutionPreset(3840, 2160);
            }

            if (GUILayout.Button("9:16", GUILayout.Width(82)))
            {
                ApplyChartRenderResolutionPreset(1080, 1920);
            }

            if (GUILayout.Button("21:9", GUILayout.Width(82)))
            {
                ApplyChartRenderResolutionPreset(2560, 1080);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(Text("chartRenderResolutionPresetHint"), hintStyle);
        }

        private void DrawFpsPresetRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Text("chartRenderFpsPreset"), labelStyle, GUILayout.Width(190));
            if (GUILayout.Button("30", GUILayout.Width(82)))
            {
                ApplyChartRenderFpsPreset(30);
            }

            if (GUILayout.Button("60", GUILayout.Width(82)))
            {
                ApplyChartRenderFpsPreset(60);
            }

            if (GUILayout.Button("120", GUILayout.Width(82)))
            {
                ApplyChartRenderFpsPreset(120);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(Text("chartRenderFpsPresetHint"), hintStyle);
        }

        private void ApplyChartRenderResolutionPreset(int width, int height)
        {
            ChartRenderWidth = width;
            ChartRenderHeight = height;
            renderWidthText = ChartRenderWidth.ToString(CultureInfo.InvariantCulture);
            renderHeightText = ChartRenderHeight.ToString(CultureInfo.InvariantCulture);
        }

        private void ApplyChartRenderFpsPreset(int fps)
        {
            ChartRenderFps = Mathf.Clamp(fps, MinChartRenderFps, MaxChartRenderFps);
            renderFpsText = ChartRenderFps.ToString(CultureInfo.InvariantCulture);
        }

        private static string DrawChoiceSettingRow(string label, string hint, string value, string[] values, string[] labels, string defaultValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(190));
            int current = IndexOf(values, value);
            if (current < 0)
            {
                current = IndexOf(values, defaultValue);
            }

            current = Mathf.Max(0, current);
            int columns = Mathf.Clamp(labels.Length, 1, 3);
            int next = GUILayout.SelectionGrid(current, labels, columns, GUILayout.MinWidth(280), GUILayout.ExpandWidth(true));
            if (next >= 0 && next < values.Length)
            {
                value = values[next];
            }

            if (GUILayout.Button(Text("chartRenderReset"), GUILayout.Width(64)))
            {
                value = defaultValue;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(hint, hintStyle);
            return value;
        }

        private static bool DrawToggleSettingRow(string label, string hint, bool value, bool defaultValue)
        {
            GUILayout.BeginHorizontal();
            value = GUILayout.Toggle(value, label, toggleStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(Text("chartRenderReset"), GUILayout.Width(64)))
            {
                value = defaultValue;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(hint, hintStyle);
            return value;
        }

        private static int IndexOf(string[] values, string value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string[] GetEncoderModeLabels()
        {
            return new[]
            {
                Text("chartRenderEncoderAutoBalanced"),
                Text("chartRenderEncoderFastest"),
                Text("chartRenderEncoderBalanced"),
                Text("chartRenderEncoderQuality"),
                Text("chartRenderEncoderCpuCompatibility"),
                Text("chartRenderEncoderCustom")
            };
        }

        private static string[] GetCaptureFormatLabels()
        {
            return new[]
            {
                Text("chartRenderCaptureRgba"),
                Text("chartRenderCaptureBgra")
            };
        }

        private static string[] GetPreviewModeLabels()
        {
            return new[]
            {
                Text("chartRenderPreviewFull"),
                Text("chartRenderPreviewDim"),
                Text("chartRenderPreviewMinimal")
            };
        }

        private static string[] GetVideoFormatLabels()
        {
            return new[]
            {
                Text("chartRenderVideoFormatMp4"),
                Text("chartRenderVideoFormatMkv"),
                Text("chartRenderVideoFormatMov")
            };
        }

        private static string[] GetAudioFormatLabels()
        {
            return new[]
            {
                Text("chartRenderAudioFormatAac"),
                Text("chartRenderAudioFormatFlac"),
                Text("chartRenderAudioFormatAlac")
            };
        }

        private string GetBitrateHint()
        {
            int recommended = ChartRenderBitratePresets.GetRecommendedBitrateMbps(ChartRenderWidth, ChartRenderHeight, ChartRenderFps);
            int effective = ChartRenderBitratePresets.ResolveTargetBitrateMbps(ChartRenderBitrateMbps, ChartRenderWidth, ChartRenderHeight, ChartRenderFps);
            return Text("chartRenderBitrateMbpsHint")
                + " "
                + string.Format(CultureInfo.InvariantCulture, Text("chartRenderBitrateRecommendedHint"), recommended, effective);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryParseFloat(string raw, out float value)
        {
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        public static string Text(string key)
        {
            return Localization.Text(key);
        }

        public void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Save(modEntry);
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            NormalizeChartRenderSettings();
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

            NormalizeChartRenderSettings();
        }

        private void NormalizeChartRenderSettings()
        {
            ChartRenderWidth = MakeEven(Mathf.Clamp(ChartRenderWidth, MinChartRenderSize, MaxChartRenderWidth));
            ChartRenderHeight = MakeEven(Mathf.Clamp(ChartRenderHeight, MinChartRenderSize, MaxChartRenderHeight));
            ChartRenderFps = Mathf.Clamp(ChartRenderFps, MinChartRenderFps, MaxChartRenderFps);
            ChartRenderCrf = Mathf.Clamp(ChartRenderCrf, MinChartRenderCrf, MaxChartRenderCrf);
            ChartRenderBitrateMbps = Mathf.Clamp(ChartRenderBitrateMbps, ChartRenderBitratePresets.AutoBitrateMbps, ChartRenderBitratePresets.MaxBitrateMbps);
            ChartRenderPreset = string.IsNullOrWhiteSpace(ChartRenderPreset)
                ? DefaultChartRenderPreset
                : ChartRenderPreset.Trim();
            ChartRenderEncoderMode = ChartRenderOptionValues.NormalizeEncoderMode(ChartRenderEncoderMode);
            ChartRenderCaptureFormat = ChartRenderOptionValues.NormalizeCaptureFormat(ChartRenderCaptureFormat);
            ChartRenderPreviewMode = ChartRenderOptionValues.NormalizePreviewMode(ChartRenderPreviewMode);
            ChartRenderAudioFormat = ChartRenderOptionValues.NormalizeAudioFormat(ChartRenderAudioFormat);
            ChartRenderVideoFormat = ChartRenderOptionValues.NormalizeVideoFormat(ChartRenderVideoFormat);
            ChartRenderCompletionTailSeconds = Mathf.Max(0f, ChartRenderCompletionTailSeconds);
            ChartRenderAudioSyncOffsetMs = Mathf.Clamp(ChartRenderAudioSyncOffsetMs, MinChartRenderAudioSyncOffsetMs, MaxChartRenderAudioSyncOffsetMs);
        }

        private static int MakeEven(int value)
        {
            return value % 2 == 0 ? value : value + 1;
        }

        private void ResetChartRenderDefaults(UnityModManager.ModEntry modEntry)
        {
            ChartRenderWorkspaceDirectory = GetDefaultWorkspaceDirectory(modEntry);
            ChartRenderExportDirectory = GetDefaultExportDirectory(modEntry);
            ChartRenderWidth = DefaultChartRenderWidth;
            ChartRenderHeight = DefaultChartRenderHeight;
            ChartRenderFps = DefaultChartRenderFps;
            ChartRenderCrf = DefaultChartRenderCrf;
            ChartRenderBitrateMbps = DefaultChartRenderBitrateMbps;
            ChartRenderPreset = DefaultChartRenderPreset;
            ChartRenderEncoderMode = DefaultChartRenderEncoderMode;
            ChartRenderCaptureFormat = DefaultChartRenderCaptureFormat;
            ChartRenderPreviewMode = DefaultChartRenderPreviewMode;
            ChartRenderAudioFormat = DefaultChartRenderAudioFormat;
            ChartRenderVideoFormat = DefaultChartRenderVideoFormat;
            ChartRenderCompletionTailSeconds = DefaultChartRenderCompletionTailSeconds;
            ChartRenderAudioSyncOffsetMs = DefaultChartRenderAudioSyncOffsetMs;
            ChartRenderShowHitJudgments = DefaultChartRenderShowHitJudgments;
            ChartRenderUseSelectedRange = DefaultChartRenderUseSelectedRange;
            ChartRenderAdvancedSettingsExpanded = DefaultChartRenderAdvancedSettingsExpanded;
            ChartRenderProfessionalSettingsExpanded = false;
            ChartRenderCustomMuxArgs = string.Empty;
            SyncChartRenderTextFields();
        }

        private void SyncChartRenderTextFields()
        {
            renderWidthText = ChartRenderWidth.ToString(CultureInfo.InvariantCulture);
            renderHeightText = ChartRenderHeight.ToString(CultureInfo.InvariantCulture);
            renderFpsText = ChartRenderFps.ToString(CultureInfo.InvariantCulture);
            renderCrfText = ChartRenderCrf.ToString(CultureInfo.InvariantCulture);
            renderBitrateText = ChartRenderBitrateMbps.ToString(CultureInfo.InvariantCulture);
            renderPresetText = ChartRenderPreset;
            renderTailSecondsText = FormatFloat(ChartRenderCompletionTailSeconds);
            renderAudioSyncOffsetText = FormatFloat(ChartRenderAudioSyncOffsetMs);
        }

        private void DrawCloudSyncSection(UnityModManager.ModEntry modEntry)
        {
            bool steamAvailable = CloudSettingsManager.IsSteamAvailable;

            GUILayout.BeginVertical(cloudSectionBoxStyle);
            Rect cloudRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            Color cloudBg = steamAvailable ? new Color(0.12f, 0.28f, 0.46f, 0.55f) : new Color(0.32f, 0.16f, 0.16f, 0.55f);
            EditorTweaksGui.DrawRect(cloudRect, cloudBg);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Text("cloudSyncSection"), cloudTitleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!steamAvailable)
            {
                GUILayout.Label(Text("cloudSteamNotAvailable"), cloudStatusStyle);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.28f, 0.55f, 0.85f);
            if (GUILayout.Button(Text("cloudDownload"), cloudButtonStyle, GUILayout.Height(34), GUILayout.Width(180)))
            {
                DownloadFromCloud(modEntry);
            }

            GUI.backgroundColor = new Color(0.85f, 0.55f, 0.28f);
            if (GUILayout.Button(Text("cloudUpload"), cloudButtonStyle, GUILayout.Height(34), GUILayout.Width(180)))
            {
                UploadToCloud(modEntry);
            }

            GUI.backgroundColor = prevBg;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(cloudStatusMessage))
            {
                Color prevContent = GUI.contentColor;
                GUI.contentColor = cloudStatusIsError ? new Color(1f, 0.55f, 0.55f) : new Color(0.55f, 1f, 0.55f);
                GUILayout.Label(cloudStatusMessage, cloudStatusStyle);
                GUI.contentColor = prevContent;
            }

            GUILayout.EndVertical();
        }

        private static void DrawPatchCompatibilityStatus()
        {
            IReadOnlyList<PatchGroupStatus> statuses = PatchManager.Statuses;
            int activeCount = 0;
            foreach (PatchGroupStatus status in statuses)
            {
                if (status.State == PatchGroupState.Active)
                {
                    activeCount++;
                }
            }

            GUILayout.BeginVertical(cloudSectionBoxStyle);
            GUILayout.Label(Text("patchCompatibilitySection"), cloudTitleStyle);

            string summaryKey = activeCount == statuses.Count && !PatchManager.HasRegistrationErrors
                ? "patchCompatibilityAllAvailable"
                : "patchCompatibilityPartial";
            string summary = Text(summaryKey)
                .Replace("{active}", activeCount.ToString(CultureInfo.InvariantCulture))
                .Replace("{total}", statuses.Count.ToString(CultureInfo.InvariantCulture));
            GUILayout.Label(summary, cloudStatusStyle);
            GUILayout.Label(Text("patchCompatibilityHint"), hintStyle);

            foreach (PatchGroupStatus status in statuses)
            {
                Color previousColor = GUI.contentColor;
                GUI.contentColor = GetPatchStatusColor(status.State);
                GUILayout.Label(BuildPatchStatusText(status), cloudStatusStyle);
                GUI.contentColor = previousColor;
            }

            if (PatchManager.HasRegistrationErrors)
            {
                Color previousColor = GUI.contentColor;
                GUI.contentColor = new Color(1f, 0.55f, 0.55f);
                GUILayout.Label(Text("patchRegistrationError"), cloudStatusStyle);
                GUI.contentColor = previousColor;
            }

            GUILayout.EndVertical();
        }

        private static string BuildPatchStatusText(PatchGroupStatus status)
        {
            string stateText;
            switch (status.State)
            {
                case PatchGroupState.Active:
                    stateText = Text("patchStateActive");
                    break;
                case PatchGroupState.Failed:
                    stateText = Text("patchStateFailed");
                    break;
                case PatchGroupState.Blocked:
                    stateText = Text("patchStateBlocked");
                    break;
                default:
                    stateText = Text("patchStateInactive");
                    break;
            }

            string text = "• " + Text(status.DisplayNameKey) + ": " + stateText;
            if (status.State == PatchGroupState.Failed)
            {
                string patchName = GetShortPatchName(status.FailedPatchName);
                string reason = CompactPatchReason(status.Reason);
                if (!string.IsNullOrEmpty(patchName))
                {
                    text += " (" + patchName + ")";
                }

                if (!string.IsNullOrEmpty(reason))
                {
                    text += " — " + reason;
                }
            }
            else if (status.State == PatchGroupState.Blocked && status.BlockedBy.HasValue)
            {
                PatchGroupStatus? dependency = PatchManager.GetStatus(status.BlockedBy.Value);
                if (dependency != null)
                {
                    text += " — " + Text("patchBlockedBy") + " " + Text(dependency.DisplayNameKey);
                }
            }

            return text;
        }

        private static Color GetPatchStatusColor(PatchGroupState state)
        {
            switch (state)
            {
                case PatchGroupState.Active:
                    return new Color(0.55f, 1f, 0.55f);
                case PatchGroupState.Failed:
                    return new Color(1f, 0.55f, 0.55f);
                case PatchGroupState.Blocked:
                    return new Color(1f, 0.78f, 0.42f);
                default:
                    return new Color(0.72f, 0.72f, 0.72f);
            }
        }

        private static string GetShortPatchName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return string.Empty;
            }

            int separator = Mathf.Max(fullName.LastIndexOf('.'), fullName.LastIndexOf('+'));
            return separator >= 0 && separator + 1 < fullName.Length
                ? fullName.Substring(separator + 1)
                : fullName;
        }

        private static string CompactPatchReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return string.Empty;
            }

            string compact = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
            const int maxLength = 160;
            return compact.Length <= maxLength ? compact : compact.Substring(0, maxLength - 1) + "…";
        }

        private void UploadToCloud(UnityModManager.ModEntry modEntry)
        {
            NormalizeChartRenderSettings();
            Save(this, modEntry);
            bool success = CloudSettingsManager.WriteToCloud(this);
            cloudStatusMessage = success ? Text("cloudUploadSuccess") : Text("cloudUploadFailed");
            cloudStatusIsError = !success;
        }

        private void DownloadFromCloud(UnityModManager.ModEntry modEntry)
        {
            if (!CloudSettingsManager.HasCloudFile())
            {
                cloudStatusMessage = Text("cloudNoFile");
                cloudStatusIsError = true;
                return;
            }

            bool success = CloudSettingsManager.TryReadFromCloud(this);
            if (success)
            {
                Save(this, modEntry);
                EnsureTextFields();
                SyncChartRenderTextFields();
            }

            cloudStatusMessage = success ? Text("cloudDownloadSuccess") : Text("cloudDownloadFailed");
            cloudStatusIsError = !success;
        }

        private static string GetDefaultWorkspaceDirectory(UnityModManager.ModEntry modEntry)
        {
            return Path.Combine(modEntry.Path, "Workspace");
        }

        private static string GetDefaultExportDirectory(UnityModManager.ModEntry modEntry)
        {
            string videos = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyVideos);
            return string.IsNullOrWhiteSpace(videos)
                ? Path.Combine(GetDefaultWorkspaceDirectory(modEntry), "Exports")
                : Path.Combine(videos, "ADOFAI Renders");
        }

        public static Settings Load(UnityModManager.ModEntry modEntry)
        {
            return Load<Settings>(modEntry);
        }

        private static void OpenHelpFile(UnityModManager.ModEntry modEntry)
        {
            string helpPath = Path.Combine(modEntry.Path, "Resources", "FFmpegReference.html");
            if (File.Exists(helpPath))
            {
                System.Diagnostics.Process.Start(helpPath);
            }
            else
            {
                Main.Log("Help file not found: " + helpPath);
            }
        }

        private static class EditorTweaksGui
        {
            private static Texture2D? pixel;

            public static void DrawRect(Rect rect, Color color)
            {
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }

                if (pixel == null)
                {
                    pixel = new Texture2D(1, 1);
                    pixel.SetPixel(0, 0, Color.white);
                    pixel.Apply();
                }

                Color previous = GUI.color;
                GUI.color = color;
                GUI.DrawTexture(rect, pixel);
                GUI.color = previous;
            }
        }
    }
}
