using System;
using System.IO;
using ADOFAI.EditorTweaks.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks
{
    /// <summary>
    /// 游戏里的界面：从 Mod 的 Resources/editortweaks.ui（Unity 打出来的 AssetBundle）里读样式、布局和 PanelSettings，
    /// 建一个带 UIDocument 的常驻物体，上面两层：下层是编辑器右侧的 <see cref="SidePanel"/>（多人制谱等），
    /// 上层是 F8 呼出的 <see cref="SettingsWindow"/>。界面本身的代码在 Src/UI。
    /// 用到时才创建；Mod 被停用时整个销毁。
    /// </summary>
    internal static class SettingsUi
    {
        private const string BundleFile = "editortweaks.ui";
        private const string PanelAsset = "Assets/EditorTweaksUI/EditorTweaksPanel.asset";
        private const string LayoutAsset = "Assets/EditorTweaksUI/Settings.uxml";
        private const string SidePanelAsset = "Assets/EditorTweaksUI/SidePanel.uxml";

        private static AssetBundle? bundle;
        private static GameObject? host;
        private static UIDocument? document;
        private static VisualElement? sideLayer;
        private static VisualElement? windowLayer;
        private static GameObject? hotkeyHost;
        private static SettingsWindow? window;
        private static SidePanel? sidePanel;

        /// <summary>上一次创建界面失败的原因；没出过错是 null。</summary>
        public static string? LastError { get; private set; }

        /// <summary>Mod 启用时调用：放一个常驻物体，监听呼出设置界面的按键，并在进出编辑器时显示 / 隐藏侧边面板。</summary>
        public static void Enable()
        {
            if (hotkeyHost != null) return;
            hotkeyHost = new GameObject("ADOFAI.EditorTweaks Hotkey");
            UnityEngine.Object.DontDestroyOnLoad(hotkeyHost);
            hotkeyHost.AddComponent<SettingsUiHotkey>();
        }

        /// <summary>Mod 停用时调用：销毁按键监听和整个界面。</summary>
        public static void Disable()
        {
            if (hotkeyHost != null) UnityEngine.Object.Destroy(hotkeyHost);
            hotkeyHost = null;
            Unload();
        }

        public static void Open()
        {
            if (EnsureWindow()) window!.Open();
        }

        public static void Toggle()
        {
            if (EnsureWindow()) window!.Toggle();
        }

        /// <summary>拉出右侧面板并切到指定分类（比如客人加入房间时切到多人制谱）。</summary>
        public static void OpenSidePanel(string category)
        {
            if (!EnsureSidePanel()) return;
            UpdateSidePanel();
            sidePanel!.Open(category);
        }

        /// <summary>鼠标是不是在我们的界面上（设置窗口或侧边面板）。是的话编辑器不该响应这次点击 / 滚轮。</summary>
        public static bool IsPointerOverUi()
        {
            try
            {
                IPanel? panel = document != null ? document.rootVisualElement?.panel : null;
                if (panel == null) return false;
                Vector2 mouse = Input.mousePosition;
                Vector2 position = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mouse.x, Screen.height - mouse.y));
                return panel.Pick(position) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Unload()
        {
            window = null;
            sidePanel = null;
            sideLayer = null;
            windowLayer = null;
            document = null;
            if (host != null) UnityEngine.Object.Destroy(host);
            host = null;
            if (bundle != null) bundle.Unload(true);
            bundle = null;
        }

        private static bool EnsureWindow()
        {
            if (window != null) return true;
            if (!EnsureDocument()) return false;
            try
            {
                var layout = Load<VisualTreeAsset>(LayoutAsset);
                window = new SettingsWindow(windowLayer!, layout, new SettingsAccess(Main.Settings), Main.Mod!.Info.Version, SaveSettings);
                return true;
            }
            catch (Exception exception)
            {
                return Failed("settings window", exception);
            }
        }

        private static bool EnsureSidePanel()
        {
            if (sidePanel != null) return true;
            if (!EnsureDocument()) return false;
            try
            {
                var layout = Load<VisualTreeAsset>(SidePanelAsset);
                sidePanel = new SidePanel(sideLayer!, layout, Coop.CoopPage.Build())
                {
                    People = Coop.CoopSession.People,
                    StatusText = () => Coop.CoopSession.StatusText,
                    StatusTone = Coop.CoopPage.StatusTone,
                    Live = () => Coop.CoopSession.InSession,
                    Attention = () => Coop.CoopSession.State == Coop.CoopState.ChoosingFolder,
                };
                return true;
            }
            catch (Exception exception)
            {
                return Failed("side panel", exception);
            }
        }

        private static bool EnsureDocument()
        {
            if (document != null && host != null) return true;
            Unload();
            try
            {
                string path = Path.Combine(Main.Mod!.Path, "Resources", BundleFile);
                if (!File.Exists(path))
                {
                    LastError = "找不到界面资源 " + path;
                    return false;
                }

                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    LastError = "界面资源加载失败（可能是 Unity 版本不匹配）：" + path;
                    return false;
                }

                var panel = Load<PanelSettings>(PanelAsset);

                // 先不激活，等 PanelSettings 设好再激活，UIDocument 才会用它建面板
                host = new GameObject("ADOFAI.EditorTweaks UI");
                host.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(host);
                document = host.AddComponent<UIDocument>();
                document.panelSettings = panel;
                host.SetActive(true);

                VisualElement root = document.rootVisualElement;
                root.style.flexGrow = 1f;
                root.pickingMode = PickingMode.Ignore;
                UiFonts.Apply(root);
                sideLayer = Layer(root);
                windowLayer = Layer(root);
                LastError = null;
                return true;
            }
            catch (Exception exception)
            {
                return Failed("UI document", exception);
            }
        }

        private static T Load<T>(string asset) where T : UnityEngine.Object
        {
            T loaded = bundle!.LoadAsset<T>(asset);
            if (loaded == null) throw new InvalidOperationException("界面资源里没有 " + asset + "（请更新 Resources/editortweaks.ui）");
            return loaded;
        }

        private static VisualElement Layer(VisualElement root)
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0f;
            layer.style.right = 0f;
            layer.style.top = 0f;
            layer.style.bottom = 0f;
            root.Add(layer);
            return layer;
        }

        private static bool Failed(string what, Exception exception)
        {
            LastError = exception.Message;
            Main.Mod?.Logger.Error("Failed to create " + what + ": " + exception);
            Unload();
            return false;
        }

        /// <summary>侧边面板只在关卡编辑器里（且不在试玩时）显示；客人加入房间要选谱面放哪时也显示。</summary>
        private static void UpdateSidePanel()
        {
            scnEditor? editor = Coop.CoopEditor.Editor;
            bool inEditor = editor != null && !editor.playMode;
            bool needed = inEditor || Coop.CoopSession.State == Coop.CoopState.ChoosingFolder;
            if (sidePanel == null)
            {
                if (!needed || LastError != null) return;
                if (!EnsureSidePanel()) return;
            }

            sidePanel!.SetAvailable(needed);
        }

        private static void SaveSettings()
        {
            if (Main.Mod != null) Main.Settings.Save(Main.Mod);
        }

        /// <summary>每帧检查呼出键（Settings.OpenSettingsKey，默认 F8），按一下开 / 关设置界面。</summary>
        private sealed class SettingsUiHotkey : MonoBehaviour
        {
            private void Update()
            {
                try
                {
                    if (Input.GetKeyDown(Main.Settings.OpenSettingsKey)) Toggle();
                    UpdateSidePanel();
                }
                catch (Exception exception)
                {
                    // 游戏的输入设置不允许读键盘就别每帧报错
                    Main.Mod?.Logger.Error("Settings hotkey disabled: " + exception.Message);
                    enabled = false;
                }
            }
        }

        /// <summary>让界面库读写 Mod 的 Settings（界面库本身不依赖 UnityModManager）。</summary>
        private sealed class SettingsAccess : IEditorTweaksSettings
        {
            private readonly Settings settings;

            public SettingsAccess(Settings settings)
            {
                this.settings = settings;
            }

            public bool EnableNumericDrag { get => settings.EnableNumericDrag; set => settings.EnableNumericDrag = value; }
            public float FloatStepPerPixel { get => settings.FloatStepPerPixel; set => settings.FloatStepPerPixel = value; }
            public float IntStepPerPixel { get => settings.IntStepPerPixel; set => settings.IntStepPerPixel = value; }
            public int MaxFloatingPoints { get => settings.MaxFloatingPoints; set => settings.MaxFloatingPoints = value; }
            public bool EnableCameraRelativeDecorationDragFix { get => settings.EnableCameraRelativeDecorationDragFix; set => settings.EnableCameraRelativeDecorationDragFix = value; }
            public bool EnableDecorationPivotFix { get => settings.EnableDecorationPivotFix; set => settings.EnableDecorationPivotFix = value; }
            public float DecorationMoveSnapStep { get => settings.DecorationMoveSnapStep; set => settings.DecorationMoveSnapStep = value; }
            public bool EnableVideoBackgroundSyncFix { get => settings.EnableVideoBackgroundSyncFix; set => settings.EnableVideoBackgroundSyncFix = value; }
            public bool PersistEditorPreferences { get => settings.PersistEditorPreferences; set => settings.PersistEditorPreferences = value; }
            public bool EnableImageLoadErrorDeduplication { get => settings.EnableImageLoadErrorDeduplication; set => settings.EnableImageLoadErrorDeduplication = value; }

            public void ResetToDefaults()
            {
                var defaults = new Settings();
                EnableNumericDrag = defaults.EnableNumericDrag;
                FloatStepPerPixel = defaults.FloatStepPerPixel;
                IntStepPerPixel = defaults.IntStepPerPixel;
                MaxFloatingPoints = defaults.MaxFloatingPoints;
                EnableCameraRelativeDecorationDragFix = defaults.EnableCameraRelativeDecorationDragFix;
                EnableDecorationPivotFix = defaults.EnableDecorationPivotFix;
                DecorationMoveSnapStep = defaults.DecorationMoveSnapStep;
                EnableVideoBackgroundSyncFix = defaults.EnableVideoBackgroundSyncFix;
                PersistEditorPreferences = defaults.PersistEditorPreferences;
                EnableImageLoadErrorDeduplication = defaults.EnableImageLoadErrorDeduplication;
            }
        }
    }
}
