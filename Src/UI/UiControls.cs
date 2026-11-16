#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>
    /// 并排的选项按钮（“开 | 关”这种）：选中的那块高亮会滑过去，文字颜色跟着变。
    /// 每个按钮固定宽度，所以高亮的位置可以直接算出来。
    /// </summary>
    public sealed class ChoiceControl : VisualElement
    {
        public const float ButtonWidth = 96f;
        public const float Gap = 6f;

        private readonly List<VisualElement> _buttons = new List<VisualElement>();
        private readonly VisualElement _highlight;
        private int _index = -1;

        public event Action<int> Changed;

        public int Index => _index;

        public int Count => _buttons.Count;

        public ChoiceControl(params string[] options)
        {
            AddToClassList("st-choices");
            _highlight = new VisualElement { pickingMode = PickingMode.Ignore };
            _highlight.AddToClassList("st-choice-hl");
            Add(_highlight);
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var button = new VisualElement();
                button.AddToClassList("st-choice");
                button.Add(new Label(options[i]) { pickingMode = PickingMode.Ignore });
                button.RegisterCallback<ClickEvent>(_ => Select(index));
                Add(button);
                _buttons.Add(button);
            }
        }

        public void SetIndexWithoutNotify(int index, bool animate)
        {
            index = Mathf.Clamp(index, 0, _buttons.Count - 1);
            if (!animate)
            {
                // 瞬间移过去：临时关掉过渡
                _highlight.AddToClassList("no-anim");
                _highlight.schedule.Execute(() => _highlight.RemoveFromClassList("no-anim")).StartingIn(40);
            }

            _index = index;
            _highlight.style.left = Gap + index * (ButtonWidth + Gap);
            for (int i = 0; i < _buttons.Count; i++) _buttons[i].EnableInClassList("on", i == index);
        }

        public void Nudge(int direction)
        {
            Select(Mathf.Clamp(_index + direction, 0, _buttons.Count - 1));
        }

        private void Select(int index)
        {
            if (index == _index) return;
            SetIndexWithoutNotify(index, animate: true);
            Changed?.Invoke(index);
        }
    }

    /// <summary>
    /// 滑块：一条细线加一个菱形滑块（悬停放大，拖动时更大）。
    /// 点轨道会平滑滑过去；支持对数刻度（步长这种跨好几个数量级的数）。
    /// </summary>
    public sealed class AnimatedSlider : VisualElement
    {
        private const float RailInset = 14f;

        private readonly VisualElement _hit;
        private readonly VisualElement _rail;
        private readonly VisualElement _thumb;
        private readonly Tweener _move;
        private readonly float _min;
        private readonly float _max;
        private readonly bool _log;
        private readonly Func<float, float> _snap;
        private readonly float _step;
        private float _value;
        private float _shown;
        private bool _drag;

        public event Action<float> Changed;

        public float Value => _value;

        /// <param name="step">线性刻度时键盘每按一下的增量；对数刻度时忽略。</param>
        /// <param name="snap">把任意数吸到合法值上（比如保留几位小数）。</param>
        public AnimatedSlider(float min, float max, bool log, float step, Func<float, float> snap)
        {
            _min = min;
            _max = max;
            _log = log;
            _step = step;
            _snap = snap ?? (v => v);
            AddToClassList("sl");

            _hit = new VisualElement();
            _hit.AddToClassList("sl-hit");
            Add(_hit);
            _rail = new VisualElement { pickingMode = PickingMode.Ignore };
            _rail.AddToClassList("sl-rail");
            _hit.Add(_rail);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("sl-track");
            _rail.Add(track);
            _thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            _thumb.AddToClassList("sl-thumb");
            _rail.Add(_thumb);
            _move = new Tweener(this);

            _hit.RegisterCallback<PointerDownEvent>(OnDown);
            _hit.RegisterCallback<PointerMoveEvent>(OnMove);
            _hit.RegisterCallback<PointerUpEvent>(OnUp);
            _hit.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            _hit.RegisterCallback<PointerEnterEvent>(_ => AddToClassList("hover"));
            _hit.RegisterCallback<PointerLeaveEvent>(_ => RemoveFromClassList("hover"));
        }

        /// <summary>设值：animate 为 true 时滑块平滑滑过去。</summary>
        public void SetValueWithoutNotify(float value, bool animate)
        {
            value = Mathf.Clamp(_snap(value), _min, _max);
            _value = value;
            MoveTo(ToNorm(value), animate);
        }

        public void Nudge(int direction)
        {
            float next = _log
                ? FromNorm(Mathf.Clamp01(ToNorm(_value) + direction * 0.03f))
                : _value + direction * _step;
            Commit(next, animate: true);
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            _drag = true;
            _hit.CapturePointer(e.pointerId);
            AddToClassList("dragging");
            Commit(FromLocalX(e.localPosition.x), animate: true);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (!_drag) return;
            Commit(FromLocalX(e.localPosition.x), animate: false);
        }

        private void OnUp(PointerUpEvent e)
        {
            if (!_drag) return;
            _hit.ReleasePointer(e.pointerId);
            EndDrag();
        }

        private void EndDrag()
        {
            _drag = false;
            RemoveFromClassList("dragging");
        }

        private void Commit(float raw, bool animate)
        {
            float value = Mathf.Clamp(_snap(Mathf.Clamp(raw, _min, _max)), _min, _max);
            if (Mathf.Approximately(value, _value)) return;
            _value = value;
            MoveTo(ToNorm(value), animate);
            Changed?.Invoke(value);
        }

        private void MoveTo(float norm, bool animate)
        {
            if (!animate || panel == null)
            {
                _move.Cancel();
                Show(norm);
                return;
            }

            float from = _shown;
            _move.Run(0.32f, t => Show(Mathf.LerpUnclamped(from, norm, t)), UiEase.OutCubic);
        }

        private void Show(float norm)
        {
            _shown = norm;
            _thumb.style.left = Length.Percent(Mathf.Clamp01(norm) * 100f);
        }

        private float FromLocalX(float x)
        {
            float width = Mathf.Max(1f, _hit.resolvedStyle.width - RailInset * 2f);
            return FromNorm(Mathf.Clamp01((x - RailInset) / width));
        }

        private float ToNorm(float value)
        {
            if (_max <= _min) return 0f;
            if (_log && _min > 0f) return Mathf.Clamp01(Mathf.Log(value / _min) / Mathf.Log(_max / _min));
            return Mathf.Clamp01((value - _min) / (_max - _min));
        }

        private float FromNorm(float norm)
        {
            if (_log && _min > 0f) return _min * Mathf.Pow(_max / _min, norm);
            return Mathf.Lerp(_min, _max, norm);
        }
    }

    /// <summary>步进器：[←] 数值 [→]，数值上下翻滚；到头了还按，数值左右抖一下。</summary>
    public sealed class StepperControl : VisualElement
    {
        private readonly int _min;
        private readonly int _max;
        private readonly Func<int, string> _format;
        private readonly VisualElement _left;
        private readonly VisualElement _right;
        private readonly VisualElement _viewport;
        private readonly Tweener _roll;
        private Label _current;
        private Label _spare;
        private int _value;

        public event Action<int> Changed;

        public int Value => _value;

        public StepperControl(int min, int max, Func<int, string> format)
        {
            _min = min;
            _max = max;
            _format = format ?? (v => v.ToString());
            AddToClassList("stp");

            _left = MakeButton("arrow-left", -1);
            Add(_left);

            _viewport = new VisualElement { pickingMode = PickingMode.Ignore };
            _viewport.AddToClassList("stp-view");
            _current = NewValueLabel();
            _spare = NewValueLabel();
            _spare.style.opacity = 0f;
            _viewport.Add(_current);
            _viewport.Add(_spare);
            Add(_viewport);

            _right = MakeButton("arrow-right", 1);
            Add(_right);
            _roll = new Tweener(_viewport);
        }

        public void SetValueWithoutNotify(int value, bool animate)
        {
            value = Mathf.Clamp(value, _min, _max);
            int previous = _value;
            _value = value;
            RefreshArrows();
            if (!animate || panel == null || previous == value)
            {
                _roll.Cancel();
                _current.text = _format(value);
                _current.style.opacity = 1f;
                ResetLabel(_current);
                ResetLabel(_spare);
                _spare.style.opacity = 0f;
                _viewport.style.translate = new Translate(0f, 0f);
                return;
            }

            Roll(value > previous ? 1 : -1, _format(value));
        }

        public void Nudge(int direction)
        {
            int next = Mathf.Clamp(_value + direction, _min, _max);
            if (next == _value)
            {
                Shake();
                return;
            }

            SetValueWithoutNotify(next, animate: true);
            Changed?.Invoke(next);
        }

        private VisualElement MakeButton(string icon, int direction)
        {
            var button = new VisualElement();
            button.AddToClassList("stp-btn");
            button.Add(UiIcon.Create(icon));
            button.RegisterCallback<ClickEvent>(_ => Nudge(direction));
            return button;
        }

        private static Label NewValueLabel()
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("stp-value");
            return label;
        }

        private static void ResetLabel(VisualElement label)
        {
            label.style.translate = new Translate(0f, 0f);
        }

        private void Roll(int direction, string text)
        {
            const float distance = 30f;
            Label outgoing = _current;
            Label incoming = _spare;
            incoming.text = text;
            incoming.style.opacity = 0f;
            incoming.style.translate = new Translate(0f, direction * distance);
            _current = incoming;
            _spare = outgoing;
            _roll.Run(0.3f, t =>
            {
                outgoing.style.translate = new Translate(0f, -direction * distance * t);
                outgoing.style.opacity = 1f - t;
                incoming.style.translate = new Translate(0f, direction * distance * (1f - t));
                incoming.style.opacity = t;
            }, UiEase.OutCubic, 0f, () =>
            {
                outgoing.style.opacity = 0f;
                ResetLabel(outgoing);
            });
        }

        private void Shake()
        {
            _roll.Run(0.28f, t =>
            {
                float x = Mathf.Sin(t * Mathf.PI * 4f) * (1f - t) * 7f;
                _viewport.style.translate = new Translate(x, 0f);
            }, UiEase.Linear, 0f, () => _viewport.style.translate = new Translate(0f, 0f));
        }

        private void RefreshArrows()
        {
            _left.EnableInClassList("off", _value <= _min);
            _right.EnableInClassList("off", _value >= _max);
        }
    }
}
