using System.Globalization;
using UnityModManagerNet;
using UnityEngine;

namespace ADOFAI.EditorTweaks
{
    public class Settings : UnityModManager.ModSettings
    {
        public bool EnableNumericDrag = true;

        public bool EnableCameraRelativeDecorationDragFix = true;

        public bool EnableDecorationPivotFix = true;

        public bool EnableVideoBackgroundSyncFix = true;

        public bool PersistEditorPreferences = true;

        public bool EnableImageLoadErrorDeduplication = true;

        public float DecorationMoveSnapStep = 0.5f;

        public float FloatStepPerPixel = 0.1f;

        public float IntStepPerPixel = 1f;

        public int MaxFloatingPoints = 3;

        /// <summary>多人制谱：麦克风模式（0 关闭，1 自由麦，2 按住说话）。</summary>
        public int VoiceMode = 0;

        /// <summary>多人制谱：上次打开麦克风时用的模式（聊天页的麦克风按钮在"关"和它之间切换）。</summary>
        public int VoiceLastMode = 1;

        /// <summary>多人制谱：按住说话的按键（默认鼠标侧键）。</summary>
        public KeyCode VoicePushKey = KeyCode.Mouse3;

        /// <summary>多人制谱：别人语音的音量（0~2）。</summary>
        public float VoiceVolume = 1f;

        /// <summary>多人制谱：收到聊天消息时在编辑器里弹提示。</summary>
        public bool CoopChatNotify = true;

        /// <summary>多人制谱：房间只有受邀的人能进（否则好友都能从好友列表加入）。</summary>
        public bool CoopInviteOnly = false;

        /// <summary>多人制谱：房间最多几个人（含房主）。</summary>
        public int CoopMaxMembers = 8;

        /// <summary>多人制谱：自动校对（发现和房主不一致时重新同步整份谱面）。</summary>
        public bool CoopAutoVerify = true;

        /// <summary>多人制谱：下载别人谱面的目录（空 = Mod 数据目录下的 CoopLevels）。</summary>
        public string CoopDownloadDir = string.Empty;

        /// <summary>多人制谱：打开 / 收起右侧面板的按键。</summary>
        public KeyCode CoopPanelKey = KeyCode.F9;

        /// <summary>呼出设置界面的按键。</summary>
        public KeyCode OpenSettingsKey = KeyCode.F8;

        /// <summary>UMM 里只放一个入口按钮，真正的设置在游戏里弹出的界面（SettingsUi）。界面加载失败时退回下面这套简易设置。</summary>
        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("在游戏里按 " + OpenSettingsKey + " 也可以打开设置界面。");
            if (GUILayout.Button("打开设置界面", GUILayout.Height(40f)))
            {
                SettingsUi.Open();
            }

            if (SettingsUi.LastError != null)
            {
                GUILayout.Label("设置界面加载失败，已显示简易设置：" + SettingsUi.LastError);
                DrawClassic();
            }

            GUILayout.EndVertical();
        }

        private void DrawClassic()
        {
            GUILayout.Label("编辑器与游戏优化");
            EnableNumericDrag = GUILayout.Toggle(EnableNumericDrag, "启用数值拖动");
            if (EnableNumericDrag)
            {
                FloatStepPerPixel = DrawFloat("浮点数每像素步长", FloatStepPerPixel, 0.0001f);
                IntStepPerPixel = DrawFloat("整数每像素步长", IntStepPerPixel, 0.0001f);
                MaxFloatingPoints = DrawInt("最多小数位", MaxFloatingPoints, 0, 8);
            }

            EnableCameraRelativeDecorationDragFix = GUILayout.Toggle(
                EnableCameraRelativeDecorationDragFix, "修复 Camera / CameraAspect 装饰拖动");
            EnableDecorationPivotFix = GUILayout.Toggle(EnableDecorationPivotFix, "修复装饰移动吸附与轴心显示");
            DecorationMoveSnapStep = DrawFloat("装饰吸附步长", DecorationMoveSnapStep, 0f);
            EnableVideoBackgroundSyncFix = GUILayout.Toggle(EnableVideoBackgroundSyncFix, "修复视频背景同步");
            PersistEditorPreferences = GUILayout.Toggle(PersistEditorPreferences, "编辑器偏好即时保存");
            EnableImageLoadErrorDeduplication = GUILayout.Toggle(
                EnableImageLoadErrorDeduplication, "启用缺图错误去重");
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
            Normalize();
        }

        public void Normalize()
        {
            DecorationMoveSnapStep = Mathf.Max(0f, DecorationMoveSnapStep);
            FloatStepPerPixel = Mathf.Max(0.0001f, FloatStepPerPixel);
            IntStepPerPixel = Mathf.Max(0.0001f, IntStepPerPixel);
            MaxFloatingPoints = Mathf.Clamp(MaxFloatingPoints, 0, 8);

        }

        public static Settings Load(UnityModManager.ModEntry modEntry)
        {
            return Load<Settings>(modEntry);
        }

        private static float DrawFloat(string label, float value, float minimum)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180f));
            string text = GUILayout.TextField(value.ToString(CultureInfo.InvariantCulture), GUILayout.Width(100f));
            GUILayout.EndHorizontal();
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? Mathf.Max(minimum, parsed)
                : value;
        }

        private static int DrawInt(string label, int value, int minimum, int maximum)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180f));
            string text = GUILayout.TextField(value.ToString(CultureInfo.InvariantCulture), GUILayout.Width(100f));
            GUILayout.EndHorizontal();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? Mathf.Clamp(parsed, minimum, maximum)
                : value;
        }
    }
}
