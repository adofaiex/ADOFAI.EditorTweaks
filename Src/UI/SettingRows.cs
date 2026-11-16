#nullable disable
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>
    /// 设置页里的一行：左边图标 + 标题 + 说明，右边是控件（开关按钮、滑块、步进器）。
    /// 选中（悬停或键盘）时左边亮起红线，图标变亮；依赖别的开关的行，开关关掉后会收起来。
    /// </summary>
    public abstract class SettingRow
    {
        private Func<bool> _available;

        protected SettingRow(string icon, string title, string description)
        {
            Element = new VisualElement();
            Element.AddToClassList("st-row");

            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("st-row-bar");
            Element.Add(bar);

            Element.Add(UiIcon.Create(icon, "st-row-icon"));

            var labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("st-labels");
            var name = new Label(title) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("st-label");
            labels.Add(name);
            if (!string.IsNullOrEmpty(description))
            {
                var note = new Label(description) { pickingMode = PickingMode.Ignore };
                note.AddToClassList("st-hint-text");
                labels.Add(note);
            }

            Element.Add(labels);

            Control = new VisualElement();
            Control.AddToClassList("st-control");
            Element.Add(Control);
        }

        public VisualElement Element { get; }

        protected VisualElement Control { get; }

        /// <summary>改了值（窗口据此刷新依赖关系，并安排保存）。</summary>
        public Action Changed { get; set; }

        /// <summary>现在是否可用（依赖的开关关了就不可用，行会收起来）。</summary>
        public bool IsAvailable => _available == null || _available();

        public bool IsCollapsed { get; private set; }

        /// <summary>依赖别的开关：条件为假时这一行收起来。</summary>
        public SettingRow AvailableWhen(Func<bool> condition)
        {
            _available = condition;
            Element.AddToClassList("sub");
            return this;
        }

        /// <summary>按当前设置刷新控件（不触发 Changed）。</summary>
        public abstract void Refresh(bool animate);

        /// <summary>键盘左右调一格。</summary>
        public virtual void Nudge(int direction) { }

        /// <summary>回车 / 空格。</summary>
        public virtual void Activate() { }

        public void SetSelected(bool selected)
        {
            Element.EnableInClassList("selected", selected);
        }

        public void SetCollapsed(bool collapsed)
        {
            IsCollapsed = collapsed;
            Element.EnableInClassList("collapsed", collapsed);
        }

        protected void NotifyChanged()
        {
            Changed?.Invoke();
        }
    }

    /// <summary>开关：并排的“开 | 关”两个按钮。</summary>
    public sealed class ToggleRow : SettingRow
    {
        private readonly ChoiceControl _choice;
        private readonly Func<bool> _get;
        private readonly Action<bool> _set;

        public ToggleRow(string icon, string title, string description, Func<bool> get, Action<bool> set)
            : base(icon, title, description)
        {
            _get = get;
            _set = set;
            _choice = new ChoiceControl("开", "关");
            Control.Add(_choice);
            _choice.Changed += index =>
            {
                _set(index == 0);
                NotifyChanged();
            };
        }

        /// <summary>开关现在是开还是关。</summary>
        public bool Value => _get();

        public override void Refresh(bool animate)
        {
            _choice.SetIndexWithoutNotify(_get() ? 0 : 1, animate);
        }

        public override void Nudge(int direction)
        {
            // 向左 = 开，向右 = 关（和按钮的左右位置一致）
            _choice.Nudge(direction);
        }

        public override void Activate()
        {
            _choice.Nudge(_choice.Index == 0 ? 1 : -1);
        }
    }

    /// <summary>滑块行：细线 + 菱形滑块，右边是可以直接输入的数值框。</summary>
    public sealed class SliderRow : SettingRow
    {
        private static readonly Regex NumberPattern = new Regex(@"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        private readonly AnimatedSlider _slider;
        private readonly TextField _field;
        private readonly Label _unit;
        private readonly Func<float> _get;
        private readonly Action<float> _set;
        private readonly Func<float, string> _format;
        private readonly string _zeroText;
        private bool _revert;

        /// <param name="unit">数值后面的单位小字。</param>
        /// <param name="zeroText">值为 0 时显示的文字（比如“关闭”），没有就照常显示数字。</param>
        public SliderRow(string icon, string title, string description,
            float min, float max, bool log, float step, Func<float, float> snap,
            Func<float, string> format, string unit,
            Func<float> get, Action<float> set, string zeroText = null)
            : base(icon, title, description)
        {
            _get = get;
            _set = set;
            _format = format;
            _zeroText = zeroText;

            _slider = new AnimatedSlider(min, max, log, step, snap);
            Control.Add(_slider);

            var box = new VisualElement();
            box.AddToClassList("st-value");
            _field = new TextField { isDelayed = false };
            _field.AddToClassList("st-value-field");
            box.Add(_field);
            _unit = new Label(unit) { pickingMode = PickingMode.Ignore };
            _unit.AddToClassList("st-value-unit");
            box.Add(_unit);
            Control.Add(box);

            _slider.Changed += value =>
            {
                _set(value);
                ShowValue(value);
                NotifyChanged();
            };

            _field.RegisterCallback<FocusInEvent>(_ =>
            {
                box.AddToClassList("editing");
                _revert = false;
                _field.SetValueWithoutNotify(_get().ToString("0.####", CultureInfo.InvariantCulture));
                _field.schedule.Execute(() => _field.SelectAll()).StartingIn(1);
            });
            _field.RegisterCallback<FocusOutEvent>(_ =>
            {
                box.RemoveFromClassList("editing");
                if (!_revert) CommitText();
                ShowValue(_get());
            });
            _field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    _field.Blur();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    _revert = true;
                    _field.Blur();
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        public override void Refresh(bool animate)
        {
            float value = _get();
            _slider.SetValueWithoutNotify(value, animate);
            ShowValue(value);
        }

        public override void Nudge(int direction)
        {
            _slider.Nudge(direction);
        }

        private void ShowValue(float value)
        {
            bool zero = _zeroText != null && Mathf.Abs(value) < 1e-6f;
            _field.SetValueWithoutNotify(zero ? _zeroText : _format(value));
            _unit.style.display = zero ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void CommitText()
        {
            Match match = NumberPattern.Match(_field.value ?? string.Empty);
            if (!match.Success || !float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) return;
            // 让滑块把数夹到合法范围并吸附，再以滑块的值为准写回设置
            _slider.SetValueWithoutNotify(parsed, animate: true);
            _set(_slider.Value);
            NotifyChanged();
        }
    }

    public sealed class StepperRow : SettingRow
    {
        private readonly StepperControl _stepper;
        private readonly Func<int> _get;
        private readonly Action<int> _set;

        public StepperRow(string icon, string title, string description, int min, int max, Func<int, string> format,
            Func<int> get, Action<int> set)
            : base(icon, title, description)
        {
            _get = get;
            _set = set;
            _stepper = new StepperControl(min, max, format);
            Control.Add(_stepper);
            _stepper.Changed += value =>
            {
                _set(value);
                NotifyChanged();
            };
        }

        public override void Refresh(bool animate)
        {
            _stepper.SetValueWithoutNotify(_get(), animate);
        }

        public override void Nudge(int direction)
        {
            _stepper.Nudge(direction);
        }
    }
}
