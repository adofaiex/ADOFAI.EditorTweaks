#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>
    /// 设置界面能看到的设置。界面代码不直接依赖 Mod 里的 Settings 类（它依赖 UnityModManager），
    /// 这样同一份界面代码既能编进 Mod，也能编成不依赖游戏的 DLL 放进 Unity 工程里预览。
    /// </summary>
    public interface IEditorTweaksSettings
    {
        bool EnableNumericDrag { get; set; }
        float FloatStepPerPixel { get; set; }
        float IntStepPerPixel { get; set; }
        int MaxFloatingPoints { get; set; }
        bool EnableCameraRelativeDecorationDragFix { get; set; }
        bool EnableDecorationPivotFix { get; set; }
        float DecorationMoveSnapStep { get; set; }
        bool EnableVideoBackgroundSyncFix { get; set; }
        bool PersistEditorPreferences { get; set; }
        bool EnableImageLoadErrorDeduplication { get; set; }

        /// <summary>全部恢复成默认值。</summary>
        void ResetToDefaults();
    }

    public sealed class SettingsSection
    {
        public SettingsSection(string title, params SettingRow[] rows)
        {
            Title = title;
            Rows = rows;
        }

        public string Title { get; }
        public IReadOnlyList<SettingRow> Rows { get; }
    }

    public sealed class SettingsCategory
    {
        public SettingsCategory(string name, string icon, params SettingsSection[] sections)
        {
            Name = name;
            Icon = icon;
            Sections = sections;
        }

        public string Name { get; }
        public string Icon { get; }

        public IReadOnlyList<SettingsSection> Sections { get; }

        /// <summary>放在设置行上面的自定义内容（比如当前谱面卡片），可以为 null。</summary>
        public UnityEngine.UIElements.VisualElement Header { get; set; }

        /// <summary>整页自定义内容（比如修改记录），设了就不显示设置行。</summary>
        public UnityEngine.UIElements.VisualElement Content { get; set; }

        /// <summary>这一页显示着的时候每 0.25 秒调用一次（刷新卡片、记录之类）。</summary>
        public Action Refresh { get; set; }
    }

    /// <summary>设置页里有哪些分类、哪些设置、各自怎么显示。</summary>
    public static class SettingsSchema
    {
        public static List<SettingsCategory> Build(IEditorTweaksSettings s)
        {
            var dragEnabled = new ToggleRow("hand", "启用数值拖动",
                "在整数、小数、Tile、Vector2 输入框上按住鼠标右键左右拖动，就能实时调整数值。",
                () => s.EnableNumericDrag, v => s.EnableNumericDrag = v);

            var numeric = new SettingsCategory("数值拖动", "drag",
                new SettingsSection("功能", dragEnabled),
                new SettingsSection("手感",
                    new SliderRow("tune", "小数步长", "拖动 1 像素，小数变化多少",
                        0.001f, 1f, true, 0f, SnapSignificant, v => v.ToString("0.0##", CultureInfo.InvariantCulture), "/ 像素",
                        () => s.FloatStepPerPixel, v => s.FloatStepPerPixel = v).AvailableWhen(() => s.EnableNumericDrag),
                    new SliderRow("stairs", "整数步长", "拖动 1 像素，整数变化多少",
                        0.1f, 10f, true, 0f, SnapSignificant, v => v.ToString("0.0##", CultureInfo.InvariantCulture), "/ 像素",
                        () => s.IntStepPerPixel, v => s.IntStepPerPixel = v).AvailableWhen(() => s.EnableNumericDrag),
                    new StepperRow("decimal", "最多小数位", "拖动小数时最多保留几位小数。",
                        0, 8, v => v + " 位",
                        () => s.MaxFloatingPoints, v => s.MaxFloatingPoints = v).AvailableWhen(() => s.EnableNumericDrag)));

            var decoration = new SettingsCategory("装饰", "shapes",
                new SettingsSection("拖动与显示",
                    new ToggleRow("camera", "修复镜头相对装饰拖动",
                        "Camera / CameraAspect 装饰按屏幕空间拖动，鼠标移动多少，装饰就跟着动多少。",
                        () => s.EnableCameraRelativeDecorationDragFix, v => s.EnableCameraRelativeDecorationDragFix = v),
                    new ToggleRow("pivot", "修复装饰轴心显示",
                        "修正轴心十字的位置，并让屏幕相对装饰的视差数据保持正确。",
                        () => s.EnableDecorationPivotFix, v => s.EnableDecorationPivotFix = v)),
                new SettingsSection("吸附",
                    new SliderRow("snap", "移动吸附步长", "拖动装饰后，把坐标吸到这个步长的整数倍上。",
                        0f, 2f, false, 0.05f, v => Mathf.Round(v / 0.05f) * 0.05f, v => v.ToString("0.00", CultureInfo.InvariantCulture), "格",
                        () => s.DecorationMoveSnapStep, v => s.DecorationMoveSnapStep = v, "关闭")));

            var video = new SettingsCategory("视频", "film",
                new SettingsSection("同步",
                    new ToggleRow("sync", "修复视频背景同步",
                        "游戏运行时按歌曲时间校正视频背景，从中途开始播放也不会音画错位。",
                        () => s.EnableVideoBackgroundSyncFix, v => s.EnableVideoBackgroundSyncFix = v)));

            var editor = new SettingsCategory("编辑器", "tune",
                new SettingsSection("体验",
                    new ToggleRow("save", "编辑器偏好即时保存",
                        "官方编辑器里的偏好设置一改就写入磁盘，不用等到退出游戏。",
                        () => s.PersistEditorPreferences, v => s.PersistEditorPreferences = v),
                    new ToggleRow("image", "缺图错误去重",
                        "加载关卡时，同一张缺失的图片只提示一次，不再刷屏。",
                        () => s.EnableImageLoadErrorDeduplication, v => s.EnableImageLoadErrorDeduplication = v)));

            return new List<SettingsCategory> { numeric, decoration, video, editor };
        }

        /// <summary>保留 3 位有效数字（对数刻度的滑块用）。</summary>
        private static float SnapSignificant(float value)
        {
            if (value <= 0f) return 0f;
            float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(value)) - 2f);
            return Mathf.Round(value / magnitude) * magnitude;
        }
    }
}
