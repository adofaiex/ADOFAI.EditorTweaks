using System;
using System.IO;
using ADOFAI.EditorTweaks.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks
{
    /// <summary>
    /// 游戏里的设置界面：从 Mod 的 Resources/editortweaks.ui（Unity 打出来的 AssetBundle）里读样式和 PanelSettings，
    /// 建一个带 UIDocument 的物体，把 <see cref="SettingsWindow"/> 放上去。窗口本身的代码在 Src/UI。
    /// 第一次打开时才创建；Mod 被停用时整个销毁。
    /// </summary>
    internal static class SettingsUi
    {
        private const string BundleFile = "editortweaks.ui";
        private const string PanelAsset = "Assets/EditorTweaksUI/EditorTweaksPanel.asset";

        private static AssetBundle? bundle;
        private static GameObject? host;
        private static GameObject? hotkeyHost;
        private static SettingsWindow? window;

        /// <summary>上一次创建界面失败的原因；没出过错是 null。</summary>
        public static string? LastError { get; private set; }

        /// <summary>Mod 启用时调用：放一个常驻物体来监听呼出设置界面的按键。</summary>
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
            if (EnsureCreated()) window!.Open();
        }

        public static void Toggle()
        {
            if (EnsureCreated()) window!.Toggle();
        }

        public static void Unload()
        {
            window = null;
            if (host != null) UnityEngine.Object.Destroy(host);
            host = null;
            if (bundle != null) bundle.Unload(true);
            bundle = null;
        }

        private static bool EnsureCreated()
        {
            if (window != null && host != null) return true;
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

                var panel = bundle.LoadAsset<PanelSettings>(PanelAsset);
                if (panel == null)
                {
                    LastError = "界面资源里没有 PanelSettings：" + PanelAsset;
                    Unload();
                    return false;
                }

                // 先不激活，等 PanelSettings 设好再激活，UIDocument 才会用它建面板
                host = new GameObject("ADOFAI.EditorTweaks UI");
                host.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(host);
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = panel;
                host.SetActive(true);

                VisualElement root = document.rootVisualElement;
                root.style.flexGrow = 1f;
                UiFonts.Apply(root);
                window = new SettingsWindow(root, new SettingsAccess(Main.Settings), Main.Mod.Info.Version, SaveSettings);
                LastError = null;
                return true;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Main.Mod?.Logger.Error("Failed to create settings UI: " + exception);
                Unload();
                return false;
            }
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
