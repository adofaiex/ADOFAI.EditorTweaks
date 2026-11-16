#nullable disable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>
    /// 给 ScrollView 换上平滑滚动和自己画的细滚动条。
    /// <para>滚轮：不再一格跳一大段，而是把"目标位置"往前推，内容按指数缓动追上去（连续滚会叠加，停下时减速停住）。</para>
    /// <para>滚动条：隐藏 Unity 自带的（默认主题的悬停描边会让它突然变很宽、时隐时现），右边画一条细线和滑块；
    /// 悬停变粗一点，可以拖动，点轨道空白处会平滑滚过去；内容放得下时整条淡出。</para>
    /// </summary>
    public sealed class SmoothScroll
    {
        private const float WheelStep = 90f;
        private const float Sharpness = 16f;
        private const float MinThumb = 36f;

        private readonly ScrollView _view;
        private readonly VisualElement _bar;
        private readonly VisualElement _thumb;
        private float _target;
        private bool _animating;
        private bool _dragging;
        private float _dragStartPointer;
        private float _dragStartOffset;
        private float _lastTime;

        private SmoothScroll(ScrollView view)
        {
            _view = view;
            view.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            view.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            _bar = new VisualElement();
            _bar.AddToClassList("ss-bar");
            _thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            _thumb.AddToClassList("ss-thumb");
            _bar.Add(_thumb);
            // 加到 ScrollView 自己的层级里（Add 会加进内容容器，跟着内容一起滚）
            view.hierarchy.Add(_bar);

            view.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            _bar.RegisterCallback<PointerDownEvent>(OnBarDown);
            _bar.RegisterCallback<PointerMoveEvent>(OnBarMove);
            _bar.RegisterCallback<PointerUpEvent>(OnBarUp);
            _bar.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            _bar.RegisterCallback<PointerEnterEvent>(_ => _bar.AddToClassList("hover"));
            _bar.RegisterCallback<PointerLeaveEvent>(_ => _bar.RemoveFromClassList("hover"));

            view.schedule.Execute(Tick).Every(16);
        }

        public static SmoothScroll Attach(ScrollView view)
        {
            return new SmoothScroll(view);
        }

        private float MaxOffset => Mathf.Max(0f, _view.contentContainer.layout.height - _view.contentViewport.layout.height);

        /// <summary>平滑滚到某个位置（键盘选行时用）。</summary>
        public void ScrollTo(float offset)
        {
            _target = Mathf.Clamp(offset, 0f, MaxOffset);
            _animating = true;
        }

        /// <summary>平滑滚到让某个元素露出来。</summary>
        public void ScrollIntoView(VisualElement element)
        {
            if (element == null || element.parent == null) return;
            float viewport = _view.contentViewport.layout.height;
            float top = element.layout.y;
            float bottom = top + element.layout.height;
            float current = _animating ? _target : _view.scrollOffset.y;
            if (top < current) ScrollTo(top - 12f);
            else if (bottom > current + viewport) ScrollTo(bottom - viewport + 12f);
        }

        private void OnWheel(WheelEvent e)
        {
            // 自己处理，ScrollView 原来那套（一格跳一大段）不再执行
            e.StopImmediatePropagation();
            if (MaxOffset <= 0f) return;
            float start = _animating ? _target : _view.scrollOffset.y;
            // 一格滚轮的 delta 在不同设备上是 1 或 3，统一成一格 = WheelStep；触控板的小数值按比例
            ScrollTo(start + Mathf.Clamp(e.delta.y, -1f, 1f) * WheelStep);
        }

        private void Tick()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(now - _lastTime, 0f, 0.1f);
            _lastTime = now;

            float max = MaxOffset;
            Vector2 offset = _view.scrollOffset;
            if (_animating && !_dragging)
            {
                _target = Mathf.Clamp(_target, 0f, max);
                float next = Mathf.Lerp(offset.y, _target, 1f - Mathf.Exp(-Sharpness * dt));
                if (Mathf.Abs(next - _target) < 0.5f)
                {
                    next = _target;
                    _animating = false;
                }

                _view.scrollOffset = new Vector2(offset.x, next);
                offset.y = next;
            }
            else
            {
                // 别处直接改了位置（换页清零之类）：目标跟上
                _target = offset.y;
            }

            UpdateBar(offset.y, max);
        }

        private void UpdateBar(float offset, float max)
        {
            float track = _bar.layout.height;
            float viewport = _view.contentViewport.layout.height;
            float content = _view.contentContainer.layout.height;
            bool show = max > 1f && !float.IsNaN(track) && track > 0f && !float.IsNaN(viewport);
            _bar.EnableInClassList("off", !show);
            // 淡出时不要挡住下面内容的点击（不能用 display:none，否则量不到轨道高度，就再也显示不回来了）
            _bar.pickingMode = show ? PickingMode.Position : PickingMode.Ignore;
            if (!show) return;
            float thumb = Mathf.Max(MinThumb, track * viewport / Mathf.Max(1f, content));
            _thumb.style.height = thumb;
            _thumb.style.top = (track - thumb) * Mathf.Clamp01(offset / max);
        }

        // ---------------------------------------------------------------- 拖动滚动条

        private void OnBarDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            float max = MaxOffset;
            if (max <= 0f) return;
            float track = _bar.layout.height;
            float thumbTop = _thumb.layout.y, thumbHeight = _thumb.layout.height;
            float y = e.localPosition.y;
            if (y < thumbTop || y > thumbTop + thumbHeight)
            {
                // 点在轨道空白处：滚到那里（滑块中心对准鼠标）
                float ratio = Mathf.Clamp01((y - thumbHeight * 0.5f) / Mathf.Max(1f, track - thumbHeight));
                ScrollTo(ratio * max);
                e.StopPropagation();
                return;
            }

            _dragging = true;
            _animating = false;
            _dragStartPointer = e.position.y;
            _dragStartOffset = _view.scrollOffset.y;
            _bar.AddToClassList("dragging");
            _bar.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnBarMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            float max = MaxOffset;
            float track = _bar.layout.height - _thumb.layout.height;
            if (track <= 0f) return;
            float offset = _dragStartOffset + (e.position.y - _dragStartPointer) / track * max;
            offset = Mathf.Clamp(offset, 0f, max);
            _view.scrollOffset = new Vector2(_view.scrollOffset.x, offset);
            _target = offset;
        }

        private void OnBarUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _bar.ReleasePointer(e.pointerId);
            EndDrag();
        }

        private void EndDrag()
        {
            _dragging = false;
            _bar.RemoveFromClassList("dragging");
        }
    }
}
