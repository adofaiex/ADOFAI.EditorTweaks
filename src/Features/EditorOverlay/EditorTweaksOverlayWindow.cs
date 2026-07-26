using System.Globalization;
using System.IO;
using ADOFAI.EditorTweaks.Features.ChartRendering;
using ADOFAI.EditorTweaks.Patching;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.EditorOverlay
{
    internal sealed class EditorTweaksOverlayWindow : MonoBehaviour
    {
        private const int WindowId = 0x7E71A01;
        private const float WindowWidth = 400f;
        private const float MinWindowWidth = 320f;
        private const float ExpandedHeight = 286f;
        private const float CollapsedHeight = 42f;
        private const float OldDefaultX = 16f;
        private const float OldDefaultY = 96f;

        private static EditorTweaksOverlayWindow? instance;
        private static bool mouseCapturedByOverlay;
        private static int mouseCaptureReleaseFrame = -1;

        private Rect windowRect;
        private GUIStyle? windowStyle;
        private GUIStyle? titleStyle;
        private GUIStyle? labelStyle;
        private GUIStyle? hintStyle;
        private GUIStyle? inputStyle;
        private GUIStyle? buttonStyle;
        private GUIStyle? toggleStyle;
        private GUIStyle? sectionStyle;
        private GUIStyle? rowStyle;
        private Texture2D? pixel;
        private Vector2 scrollPosition;
        private ChartRenderSession? chartRenderSession;
        private string chartRenderMessage = string.Empty;
        private float drawWidth;

        private string snapStepText = string.Empty;
        private string floatStepText = string.Empty;
        private string intStepText = string.Empty;
        private string decimalsText = string.Empty;

        public static void Ensure()
        {
            if (instance != null)
            {
                return;
            }

            GameObject host = new GameObject("ADOFAI.EditorTweaks.Overlay");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<EditorTweaksOverlayWindow>();
        }

        public static void Destroy()
        {
            if (instance == null)
            {
                return;
            }

            Object.Destroy(instance.gameObject);
            instance = null;
            mouseCapturedByOverlay = false;
            mouseCaptureReleaseFrame = -1;
        }

        public static bool IsRenderOverlayActive => ChartRenderSession.IsRendering
            || (instance != null && instance.chartRenderSession != null && instance.chartRenderSession.IsActive);

        public static bool ShouldBlockEditorInput()
        {
            return IsRenderOverlayActive || ShouldBlockMouseInput();
        }

        public static bool ShouldBlockUnityUiInput()
        {
            return IsRenderOverlayActive || ShouldBlockMouseInput();
        }

        public static bool ShouldBlockGameplayInput()
        {
            return IsRenderOverlayActive;
        }

        public static bool ShouldBlockMouseInput()
        {
            if (mouseCaptureReleaseFrame >= 0 && Time.frameCount > mouseCaptureReleaseFrame)
            {
                mouseCapturedByOverlay = false;
                mouseCaptureReleaseFrame = -1;
            }

            if (instance == null || !ShouldDraw())
            {
                mouseCapturedByOverlay = false;
                mouseCaptureReleaseFrame = -1;
                return false;
            }

            bool insideOverlay = instance.IsMouseInsideWindow();
            bool mouseDown = IsAnyMouseButtonDown();
            bool mouseUp = IsAnyMouseButtonUp();
            bool mouseHeld = IsAnyMouseButtonHeld();
            bool mouseActivity = mouseDown || mouseUp || mouseHeld || HasMouseWheelActivity();

            if (mouseDown)
            {
                mouseCapturedByOverlay = insideOverlay;
            }

            bool capturedThisFrame = mouseCapturedByOverlay || mouseCaptureReleaseFrame == Time.frameCount;
            bool block = mouseActivity && (insideOverlay || capturedThisFrame);

            if (mouseUp && mouseCapturedByOverlay && !mouseHeld)
            {
                mouseCaptureReleaseFrame = Time.frameCount;
            }

            return block;
        }

        private void Awake()
        {
            windowRect = GetInitialWindowRect();
            SyncTextFields();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnGUI()
        {
            if (!ShouldDraw())
            {
                return;
            }

            EnsureStyles();
            ClampToScreen();

            Rect oldRect = windowRect;
            bool renderModalActive = chartRenderSession != null && chartRenderSession.IsActive;
            GUI.depth = -900;

            bool oldGuiEnabled = GUI.enabled;
            GUI.enabled = oldGuiEnabled && !renderModalActive;
            windowRect = GUI.Window(WindowId, windowRect, DrawWindow, GUIContent.none, windowStyle);
            GUI.enabled = oldGuiEnabled;
            windowRect.width = GetWindowWidth();
            windowRect.height = Main.Settings.EditorOverlayCollapsed ? CollapsedHeight : ExpandedHeight;

            if (renderModalActive)
            {
                DrawRenderOverlay(chartRenderSession!);
            }

            if (!renderModalActive && Vector2.Distance(oldRect.position, windowRect.position) > 0.1f)
            {
                Main.Settings.EditorOverlayX = windowRect.x;
                Main.Settings.EditorOverlayY = windowRect.y;
                SaveSettings();
            }
        }

        private static bool ShouldDraw()
        {
            if (!Main.Settings.ShowEditorOverlay)
            {
                return false;
            }

            return (ADOBase.isEditingLevel && ADOBase.editor != null)
                || ChartRenderSession.IsPlayableLevelLoaded()
                || ChartRenderSession.IsRendering;
        }

        private bool IsMouseInsideWindow()
        {
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return windowRect.Contains(guiMouse);
        }

        private static bool IsAnyMouseButtonDown()
        {
            return Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        }

        private static bool IsAnyMouseButtonHeld()
        {
            return Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
        }

        private static bool IsAnyMouseButtonUp()
        {
            return Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2);
        }

        private static bool HasMouseWheelActivity()
        {
            return Mathf.Abs(Input.mouseScrollDelta.x) > 0.01f || Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f;
        }

        private void DrawWindow(int id)
        {
            bool blockWindowInput = IsRenderOverlayActive;
            float width = windowRect.width;
            drawWidth = width;
            
            // 现代渐变背景
            DrawRect(new Rect(0f, 42f, width, Mathf.Max(0f, windowRect.height - 42f)), new Color(0.11f, 0.12f, 0.14f, 0.98f));
            // 标题栏渐变
            DrawGradientRect(new Rect(0f, 0f, width, 42f), new Color(0.16f, 0.18f, 0.22f, 0.95f), new Color(0.13f, 0.15f, 0.18f, 0.95f));
            // 标题栏底部高光线
            DrawRect(new Rect(0f, 41f, width, 1f), new Color(0.40f, 0.65f, 0.85f, 0.55f));
            // 绘制窗口边框和阴影
            DrawWindowBorder(width, windowRect.height);
            // 左侧强调色条 - 更宽更醒目
            DrawRect(new Rect(12f, 10f, 5f, 22f), new Color(0.35f, 0.70f, 0.95f, 1f));
            // 强调色条左侧微光
            DrawRect(new Rect(11f, 10f, 1f, 22f), new Color(0.50f, 0.82f, 1f, 0.5f));

            GUI.Label(new Rect(28f, 8f, width - 78f, 26f), Settings.Text("overlayTitle"), titleStyle);
            
            // 现代化的折叠按钮
            string collapseText = Main.Settings.EditorOverlayCollapsed ? "+" : "-";
            if (!blockWindowInput && GUI.Button(new Rect(width - 44f, 9f, 28f, 24f), collapseText, buttonStyle))
            {
                Main.Settings.EditorOverlayCollapsed = !Main.Settings.EditorOverlayCollapsed;
                windowRect.height = Main.Settings.EditorOverlayCollapsed ? CollapsedHeight : ExpandedHeight;
                SaveSettings();
            }

            if (Main.Settings.EditorOverlayCollapsed)
            {
                if (!blockWindowInput)
                {
                    GUI.DragWindow(new Rect(0f, 0f, width - 44f, CollapsedHeight));
                }

                return;
            }

            Rect scrollRect = new Rect(0f, 50f, width, windowRect.height - 58f);
            Rect viewRect = new Rect(0f, 0f, width - 16f, 468f);
            scrollPosition = GUI.BeginScrollView(scrollRect, scrollPosition, viewRect, false, true);
            drawWidth = viewRect.width;

            float y = 8f;
            DrawSectionLabel(Settings.Text("decorationSection"), y, Settings.Text("zeroDisables"));
            y += 32f;
            DrawFloatRow(y, Settings.Text("decorationMoveSnapStep"), ref snapStepText, 0f, value => Main.Settings.DecorationMoveSnapStep = value);

            y += 46f;
            DrawSectionLabel(Settings.Text("numericSection"), y);
            y += 32f;
            DrawFloatRow(y, Settings.Text("floatStepPerPixel"), ref floatStepText, 0.0001f, value => Main.Settings.FloatStepPerPixel = value);
            y += 38f;
            DrawFloatRow(y, Settings.Text("intStepPerPixel"), ref intStepText, 0.0001f, value => Main.Settings.IntStepPerPixel = value);
            y += 38f;
            DrawIntRow(y, Settings.Text("maxFloatDecimals"), ref decimalsText, 0, 8, value => Main.Settings.MaxFloatingPoints = value);

            y += 46f;
            DrawSectionLabel(Settings.Text("renderSection"), y);
            y += 32f;
            DrawChartRenderPanel(y);

            GUI.EndScrollView();
            drawWidth = width;
            if (!blockWindowInput)
            {
                GUI.DragWindow(new Rect(0f, 0f, width - 44f, 42f));
            }
        }

        private void DrawSectionLabel(string text, float y, string? hint = null)
        {
            float width = drawWidth > 0f ? drawWidth : windowRect.width;
            GUI.Label(new Rect(24f, y, width * 0.55f, 24f), text, sectionStyle);
            if (!string.IsNullOrEmpty(hint))
            {
                GUI.Label(new Rect(width - 112f, y + 2f, 88f, 20f), hint, hintStyle);
            }

            // 现代化分隔线 - 渐变效果
            DrawGradientRect(new Rect(24f, y + 25f, width - 48f, 1f), new Color(0.40f, 0.65f, 0.85f, 0.25f), new Color(0.40f, 0.65f, 0.85f, 0.08f));
        }

        private void DrawFloatRow(float y, string label, ref string text, float min, System.Action<float> apply)
        {
            DrawValueRow(y, label, text, out Rect inputRect);
            string next = GUI.TextField(inputRect, text, inputStyle);

            if (next == text)
            {
                return;
            }

            text = next;
            if (TryParseFloat(text, out float value))
            {
                apply(Mathf.Max(min, value));
                SaveSettings();
            }
        }

        private void DrawIntRow(float y, string label, ref string text, int min, int max, System.Action<int> apply)
        {
            DrawValueRow(y, label, text, out Rect inputRect);
            string next = GUI.TextField(inputRect, text, inputStyle);

            if (next == text)
            {
                return;
            }

            text = next;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                apply(Mathf.Clamp(value, min, max));
                SaveSettings();
            }
        }

        private void DrawValueRow(float y, string label, string value, out Rect inputRect)
        {
            float width = drawWidth > 0f ? drawWidth : windowRect.width;
            Rect rowRect = new Rect(22f, y, width - 44f, 34f);
            GUI.Box(rowRect, GUIContent.none, rowStyle);
            
            // 左侧强调色条
            DrawRect(new Rect(rowRect.x, rowRect.y + 1f, 3f, rowRect.height - 2f), new Color(0.35f, 0.65f, 0.85f, 0.45f));

            inputRect = new Rect(width - 114f, y + 6f, 86f, 22f);
            Rect labelRect = new Rect(36f, y + 7f, inputRect.x - 46f, 20f);
            GUI.Label(labelRect, label, labelStyle);
        }

        private void DrawChartRenderPanel(float y)
        {
            float width = drawWidth > 0f ? drawWidth : windowRect.width;
            Rect panelRect = new Rect(22f, y, width - 44f, 184f);
            GUI.Box(panelRect, GUIContent.none, rowStyle);
            
            // 左侧强调色条
            DrawRect(new Rect(panelRect.x, panelRect.y + 1f, 3f, panelRect.height - 2f), new Color(0.35f, 0.65f, 0.85f, 0.45f));

            string disabledReason = GetChartRenderDisabledReason();
            bool isRendering = chartRenderSession != null && chartRenderSession.IsActive;
            bool canRender = string.IsNullOrEmpty(disabledReason) && !isRendering;
            string status = canRender ? Settings.Text("chartRendererReady") : disabledReason;
            if (!string.IsNullOrEmpty(chartRenderMessage) && !isRendering)
            {
                status = chartRenderMessage;
            }

            GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 12f, panelRect.width - 32f, 34f), status, labelStyle);
            GUI.Label(new Rect(panelRect.x + 16f, panelRect.y + 46f, panelRect.width - 32f, 20f), GetChartRenderProfileText(), hintStyle);

            Rect toggleRect = new Rect(panelRect.x + 16f, panelRect.y + 72f, panelRect.width - 32f, 24f);
            bool showJudgments = GUI.Toggle(toggleRect, Main.Settings.ChartRenderShowHitJudgments, Settings.Text("chartRenderShowHitJudgments"), toggleStyle);
            if (showJudgments != Main.Settings.ChartRenderShowHitJudgments)
            {
                Main.Settings.ChartRenderShowHitJudgments = showJudgments;
                SaveSettings();
            }

            Rect rangeToggleRect = new Rect(panelRect.x + 16f, panelRect.y + 98f, panelRect.width - 32f, 24f);
            bool useSelectedRange = GUI.Toggle(rangeToggleRect, Main.Settings.ChartRenderUseSelectedRange, Settings.Text("chartRenderUseSelectedRange"), toggleStyle);
            if (useSelectedRange != Main.Settings.ChartRenderUseSelectedRange)
            {
                Main.Settings.ChartRenderUseSelectedRange = useSelectedRange;
                SaveSettings();
            }

            GUI.enabled = canRender;
            if (GUI.Button(new Rect(panelRect.x + 16f, panelRect.y + 140f, panelRect.width - 32f, 32f), Settings.Text("chartRendererRender"), buttonStyle))
            {
                StartChartRender();
            }

            GUI.enabled = true;
        }

        private static string GetChartRenderProfileText()
        {
            Settings settings = Main.Settings;
            int bitrate = ChartRenderBitratePresets.ResolveTargetBitrateMbps(settings.ChartRenderBitrateMbps, settings.ChartRenderWidth, settings.ChartRenderHeight, settings.ChartRenderFps);
            return settings.ChartRenderWidth + "x" + settings.ChartRenderHeight
                + " @ " + settings.ChartRenderFps + "fps"
                + " | " + bitrate + " Mbps"
                + " | ." + ChartRenderOptionValues.NormalizeVideoFormat(settings.ChartRenderVideoFormat)
                + " | " + GetChartRenderRangeText();
        }

        private static string GetChartRenderRangeText()
        {
            if (!Main.Settings.ChartRenderUseSelectedRange)
            {
                return Settings.Text("chartRenderSelectedRangeWholeLevel");
            }

            return ChartRenderRange.TryGetEditorSelectedRange(out int startFloor, out int endFloor, out _)
                ? Settings.Text("chartRenderSelectedRangeActive") + " " + startFloor + " - " + endFloor
                : Settings.Text("chartRendererSelectedRangeMissing");
        }

        private void StartChartRender()
        {
            if (Main.Mod == null || !PatchManager.IsAvailable(PatchFeature.ChartRendering))
            {
                return;
            }

            chartRenderMessage = string.Empty;
            chartRenderSession = new ChartRenderSession(Main.Mod, Main.Settings);
            StartCoroutine(chartRenderSession.Run(result =>
            {
                chartRenderMessage = result.Success
                    ? Settings.Text("chartRendererDone") + ": " + result.OutputPath
                    : Settings.Text("chartRendererFailed") + ": " + result.Message;
            }));
        }

        private static string GetChartRenderDisabledReason()
        {
            if (!PatchManager.IsAvailable(PatchFeature.ChartRendering))
            {
                return Settings.Text("chartRendererIncompatible");
            }

            scnEditor editor = ADOBase.editor;
            if (!ChartRenderSession.IsPlayableLevelLoaded())
            {
                return Settings.Text("chartRendererMissingLevel");
            }

            scnGame? level = editor != null ? editor.customLevel : null;
            if (editor != null && (level == null || level.levelData == null || string.IsNullOrEmpty(level.levelData.songFilename)))
            {
                return Settings.Text("chartRendererMissingSong");
            }

            if (editor == null && !ChartRenderSession.HasRenderableAudio())
            {
                return Settings.Text("chartRendererMissingSong");
            }

            if (Main.Settings.ChartRenderUseSelectedRange && !ChartRenderRange.TryGetEditorSelectedRange(out _, out _, out _))
            {
                return Settings.Text("chartRendererSelectedRangeMissing");
            }

            string ffmpeg = ChartRenderPaths.GetFfmpegPath();
            if (!File.Exists(ffmpeg))
            {
                return Settings.Text("chartRendererMissingFfmpeg");
            }

            return string.Empty;
        }

        private void DrawRenderOverlay(ChartRenderSession activeSession)
        {
            string previewMode = ChartRenderOptionValues.NormalizePreviewMode(Main.Settings.ChartRenderPreviewMode);
            float dimAlpha = previewMode == ChartRenderOptionValues.PreviewFull ? 0.72f : 0.86f;
            if (previewMode == ChartRenderOptionValues.PreviewMinimal)
            {
                dimAlpha = 0.96f;
            }

            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, dimAlpha));

            float width = Mathf.Min(620f, Screen.width - 48f);
            float height = 324f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            DrawRect(panel, new Color(0.05f, 0.06f, 0.07f, 0.96f));
            DrawRect(new Rect(panel.x, panel.y, panel.width, 1f), new Color(0.65f, 0.82f, 0.86f, 0.82f));
            DrawRect(new Rect(panel.x, panel.yMax - 1f, panel.width, 1f), new Color(0.65f, 0.82f, 0.86f, 0.82f));
            DrawRect(new Rect(panel.x, panel.y, 1f, panel.height), new Color(0.65f, 0.82f, 0.86f, 0.82f));
            DrawRect(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), new Color(0.65f, 0.82f, 0.86f, 0.82f));

            GUI.Label(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, 26f), activeSession.StageText, titleStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 54f, panel.width - 48f, 24f), activeSession.DetailText, labelStyle);

            Rect bar = new Rect(panel.x + 24f, panel.y + 92f, panel.width - 48f, 18f);
            DrawRect(bar, new Color(0.10f, 0.12f, 0.13f, 1f));
            DrawRect(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(activeSession.Progress), bar.height), new Color(0.36f, 0.75f, 0.80f, 0.95f));
            DrawRect(new Rect(bar.x, bar.y, bar.width, 1f), new Color(0.24f, 0.42f, 0.46f, 0.95f));
            DrawRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), new Color(0.24f, 0.42f, 0.46f, 0.95f));

            float duplicatePercent = activeSession.DuplicateRatio * 100f;
            float progressPercent = activeSession.Progress * 100f;
            GUI.Label(new Rect(panel.x + 24f, panel.y + 122f, panel.width - 48f, 22f), $"模式: 离线定帧 | 编码器: {activeSession.EncoderName}", labelStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 144f, panel.width - 48f, 22f), $"写入帧: {activeSession.WrittenFrames}/{activeSession.TotalFrames} ({progressPercent:0.0}%)", labelStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 166f, panel.width - 48f, 22f), $"处理速度: {activeSession.ProcessingFps:0.0} 帧/秒（只影响等待时间，不等于成品帧率）", labelStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 188f, panel.width - 48f, 22f), $"重复帧: {activeSession.DuplicateFrames} ({duplicatePercent:0.00}%) - {FormatSmoothness(activeSession.SmoothnessText)}", labelStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 210f, panel.width - 48f, 22f), $"预计剩余: {activeSession.EstimatedRemaining:hh\\:mm\\:ss}", labelStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 232f, panel.width - 48f, 22f), $"内存: {activeSession.MemoryBudgetText}", labelStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 254f, panel.width - 48f, 22f), $"队列: {activeSession.QueueBudgetText}", labelStyle);
            if (GUI.Button(new Rect(panel.x + panel.width - 144f, panel.y + panel.height - 46f, 120f, 30f), Settings.Text("chartRendererCancel"), buttonStyle))
            {
                activeSession.Cancel();
            }
        }

        private static string FormatSmoothness(string key)
        {
            switch (key)
            {
                case "excellent":
                    return "优秀，基本看不出";
                case "good":
                    return "正常，偶尔轻微重复";
                case "minor stutter":
                    return "可能轻微卡顿";
                case "visible stutter":
                    return "明显卡顿";
                default:
                    return "严重卡顿";
            }
        }

        private void SyncTextFields()
        {
            snapStepText = FormatFloat(Main.Settings.DecorationMoveSnapStep);
            floatStepText = FormatFloat(Main.Settings.FloatStepPerPixel);
            intStepText = FormatFloat(Main.Settings.IntStepPerPixel);
            decimalsText = Main.Settings.MaxFloatingPoints.ToString(CultureInfo.InvariantCulture);
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

        private void ClampToScreen()
        {
            windowRect.height = Main.Settings.EditorOverlayCollapsed ? CollapsedHeight : ExpandedHeight;
            windowRect.width = GetWindowWidth();
            float maxX = Mathf.Max(0f, Screen.width - windowRect.width - 8f);
            float maxY = Mathf.Max(0f, Screen.height - windowRect.height - 8f);
            windowRect.x = Mathf.Clamp(windowRect.x, 8f, maxX);
            windowRect.y = Mathf.Clamp(windowRect.y, 8f, maxY);
        }

        private static float GetWindowWidth()
        {
            return Mathf.Clamp(Screen.width - 16f, MinWindowWidth, WindowWidth);
        }

        private static Rect GetInitialWindowRect()
        {
            bool useDefaultPosition = Main.Settings.EditorOverlayX < 0f
                || Main.Settings.EditorOverlayY < 0f
                || (Mathf.Abs(Main.Settings.EditorOverlayX - OldDefaultX) < 0.1f
                    && Mathf.Abs(Main.Settings.EditorOverlayY - OldDefaultY) < 0.1f);

            float height = Main.Settings.EditorOverlayCollapsed ? CollapsedHeight : ExpandedHeight;
            if (!useDefaultPosition)
            {
                return new Rect(Main.Settings.EditorOverlayX, Main.Settings.EditorOverlayY, GetWindowWidth(), height);
            }

            float width = GetWindowWidth();
            float x = Mathf.Clamp(Screen.width * 0.63f, 8f, Mathf.Max(8f, Screen.width - width - 8f));
            float y = Mathf.Clamp(14f, 8f, Mathf.Max(8f, Screen.height - height - 8f));
            Main.Settings.EditorOverlayX = x;
            Main.Settings.EditorOverlayY = y;
            SaveSettings();
            return new Rect(x, y, width, height);
        }

        private void EnsureStyles()
        {
            if (windowStyle != null)
            {
                return;
            }

            // 现代深色主题配色 - 参考截图风格
            Texture2D windowBackground = MakeTexture(new Color(0.11f, 0.12f, 0.14f, 0.98f));
            windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(12, 12, 12, 12),
                normal = { background = windowBackground },
                focused = { background = windowBackground },
                active = { background = windowBackground },
                hover = { background = windowBackground },
                onNormal = { background = windowBackground },
                onFocused = { background = windowBackground },
                onActive = { background = windowBackground },
                onHover = { background = windowBackground }
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.95f, 0.97f, 1f, 1f) }
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 8, 0, 0),
                normal = { textColor = new Color(0.88f, 0.90f, 0.92f, 1f) }
            };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.55f, 0.58f, 0.62f, 0.88f) }
            };
            inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                padding = new RectOffset(6, 6, 4, 4),
                border = new RectOffset(4, 4, 4, 4),
                normal =
                {
                    background = MakeRoundedTexture(new Color(0.14f, 0.16f, 0.18f, 0.95f), 4),
                    textColor = new Color(0.92f, 0.94f, 0.96f, 1f)
                },
                hover =
                {
                    background = MakeRoundedTexture(new Color(0.16f, 0.18f, 0.20f, 0.98f), 4),
                    textColor = new Color(0.95f, 0.97f, 1f, 1f)
                },
                focused =
                {
                    background = MakeRoundedTexture(new Color(0.18f, 0.20f, 0.23f, 1f), 4),
                    textColor = new Color(0.98f, 0.99f, 1f, 1f)
                }
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(12, 12, 6, 6),
                border = new RectOffset(6, 6, 6, 6),
                normal =
                {
                    background = MakeGradientButton(new Color(0.25f, 0.50f, 0.75f, 0.92f), new Color(0.20f, 0.42f, 0.65f, 0.92f)),
                    textColor = new Color(0.95f, 0.97f, 1f, 1f)
                },
                hover =
                {
                    background = MakeGradientButton(new Color(0.30f, 0.58f, 0.85f, 0.98f), new Color(0.25f, 0.50f, 0.75f, 0.98f)),
                    textColor = Color.white
                },
                active =
                {
                    background = MakeGradientButton(new Color(0.22f, 0.45f, 0.68f, 1f), new Color(0.18f, 0.38f, 0.58f, 1f)),
                    textColor = new Color(0.90f, 0.92f, 0.95f, 1f)
                }
            };
            toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(20, 0, 3, 3),
                normal = { textColor = new Color(0.86f, 0.88f, 0.90f, 1f) },
                hover = { textColor = new Color(0.95f, 0.97f, 1f, 1f) },
                focused = { textColor = new Color(0.95f, 0.97f, 1f, 1f) },
                active = { textColor = new Color(0.95f, 0.97f, 1f, 1f) },
                onNormal = { textColor = new Color(0.30f, 0.70f, 0.95f, 1f) },
                onHover = { textColor = new Color(0.40f, 0.78f, 1f, 1f) },
                onFocused = { textColor = new Color(0.40f, 0.78f, 1f, 1f) },
                onActive = { textColor = new Color(0.40f, 0.78f, 1f, 1f) }
            };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.50f, 0.75f, 0.95f, 1f) }
            };
            rowStyle = new GUIStyle
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 2, 2),
                border = new RectOffset(4, 4, 4, 4),
                normal = { background = MakeRoundedTexture(new Color(0.14f, 0.16f, 0.18f, 0.65f), 6) }
            };
        }

        private void DrawWindowBorder(float width, float height)
        {
            // 外层发光边框
            Color glowOuter = new Color(0.50f, 0.75f, 0.95f, 0.35f);
            Color glowInner = new Color(0.35f, 0.60f, 0.80f, 0.20f);
            
            // 主边框
            Color borderMain = new Color(0.25f, 0.40f, 0.55f, 0.75f);

            // 外层发光
            DrawRect(new Rect(-1f, -1f, width + 2f, 1f), glowOuter);
            DrawRect(new Rect(-1f, height, width + 2f, 1f), glowOuter);
            DrawRect(new Rect(-1f, -1f, 1f, height + 2f), glowOuter);
            DrawRect(new Rect(width, -1f, 1f, height + 2f), glowOuter);

            // 主边框
            DrawRect(new Rect(0f, 0f, width, 1f), borderMain);
            DrawRect(new Rect(0f, height - 1f, width, 1f), borderMain);
            DrawRect(new Rect(0f, 0f, 1f, height), borderMain);
            DrawRect(new Rect(width - 1f, 0f, 1f, height), borderMain);

            // 内层高光
            DrawRect(new Rect(1f, 1f, width - 2f, 1f), glowInner);
            DrawRect(new Rect(1f, 1f, 1f, height - 2f), glowInner);
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void DrawRect(Rect rect, Color color)
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

        private void DrawGradientRect(Rect rect, Color topColor, Color bottomColor)
        {
            Texture2D gradient = MakeGradientTexture((int)rect.height, topColor, bottomColor);
            GUI.DrawTexture(rect, gradient);
            Object.Destroy(gradient);
        }

        private static Texture2D MakeGradientTexture(int height, Color topColor, Color bottomColor)
        {
            Texture2D texture = new Texture2D(1, Mathf.Max(height, 2));
            for (int i = 0; i < texture.height; i++)
            {
                float t = (float)i / (texture.height - 1);
                texture.SetPixel(0, i, Color.Lerp(topColor, bottomColor, t));
            }
            texture.Apply();
            return texture;
        }

        private static Texture2D MakeRoundedTexture(Color color, int cornerRadius)
        {
            int size = 16;
            Texture2D texture = new Texture2D(size, size);
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distToCorner = 0f;
                    if (x < cornerRadius && y < cornerRadius)
                        distToCorner = Mathf.Sqrt((cornerRadius - x) * (cornerRadius - x) + (cornerRadius - y) * (cornerRadius - y));
                    else if (x >= size - cornerRadius && y < cornerRadius)
                        distToCorner = Mathf.Sqrt((x - (size - cornerRadius - 1)) * (x - (size - cornerRadius - 1)) + (cornerRadius - y) * (cornerRadius - y));
                    else if (x < cornerRadius && y >= size - cornerRadius)
                        distToCorner = Mathf.Sqrt((cornerRadius - x) * (cornerRadius - x) + (y - (size - cornerRadius - 1)) * (y - (size - cornerRadius - 1)));
                    else if (x >= size - cornerRadius && y >= size - cornerRadius)
                        distToCorner = Mathf.Sqrt((x - (size - cornerRadius - 1)) * (x - (size - cornerRadius - 1)) + (y - (size - cornerRadius - 1)) * (y - (size - cornerRadius - 1)));
                    
                    if (distToCorner > cornerRadius && distToCorner > 0f)
                    {
                        texture.SetPixel(x, y, new Color(color.r, color.g, color.b, 0f));
                    }
                    else
                    {
                        texture.SetPixel(x, y, color);
                    }
                }
            }
            
            texture.Apply();
            return texture;
        }

        private static Texture2D MakeGradientButton(Color topColor, Color bottomColor)
        {
            int height = 32;
            Texture2D texture = new Texture2D(1, height);
            
            for (int i = 0; i < height; i++)
            {
                float t = (float)i / (height - 1);
                texture.SetPixel(0, i, Color.Lerp(topColor, bottomColor, t));
            }
            
            texture.Apply();
            return texture;
        }

        private static void SaveSettings()
        {
            if (Main.Mod != null)
            {
                Main.Settings.Save(Main.Mod);
            }
        }
    }
}
