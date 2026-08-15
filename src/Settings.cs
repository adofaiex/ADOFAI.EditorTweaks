using System.Globalization;
using ADOFAI.EditorTweaks.Patching;
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

        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.BeginVertical(GUI.skin.box);
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

            GUILayout.Space(4f);
            GUILayout.Label("兼容性状态：" + PatchManager.GetSummary());
            GUILayout.EndVertical();
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

        public void ResetAllDefaults(UnityModManager.ModEntry modEntry)
        {
            EnableNumericDrag = true;
            EnableCameraRelativeDecorationDragFix = true;
            EnableDecorationPivotFix = true;
            EnableVideoBackgroundSyncFix = true;
            PersistEditorPreferences = true;
            EnableImageLoadErrorDeduplication = true;
            DecorationMoveSnapStep = 0.5f;
            FloatStepPerPixel = 0.1f;
            IntStepPerPixel = 1f;
            MaxFloatingPoints = 3;

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
