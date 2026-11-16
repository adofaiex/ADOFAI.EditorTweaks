#nullable disable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>缓动函数。输入 0..1，输出 0..1（OutBack / OutElastic 会冲过 1 再回来）。</summary>
    public static class UiEase
    {
        public static float Linear(float t) => t;

        public static float OutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        public static float OutQuint(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u * u * u;
        }

        public static float InCubic(float t) => t * t * t;

        public static float InOutCubic(float t)
        {
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }

        public static float OutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public static float OutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c4 = 2f * Mathf.PI / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }
    }

    /// <summary>
    /// 绑在一个元素上的补间动画：同一时间只跑一个，新的会替换旧的。
    /// 用真实时间（不受游戏暂停 / 慢放影响）驱动，元素不在界面里时自动停。
    /// </summary>
    public sealed class Tweener
    {
        private readonly VisualElement _owner;
        private IVisualElementScheduledItem _item;

        public Tweener(VisualElement owner)
        {
            _owner = owner;
        }

        public bool Running => _item != null;

        public void Cancel()
        {
            _item?.Pause();
            _item = null;
        }

        /// <param name="apply">每帧调用，参数是缓动之后的进度（可能超过 1）。</param>
        public void Run(float duration, Action<float> apply, Func<float, float> ease = null, float delay = 0f, Action done = null)
        {
            Cancel();
            Func<float, float> curve = ease ?? UiEase.OutCubic;
            float start = Time.realtimeSinceStartup + delay;
            IVisualElementScheduledItem item = null;
            item = _owner.schedule.Execute(() =>
            {
                float now = Time.realtimeSinceStartup;
                if (now < start) return;
                float t = duration <= 0f ? 1f : Mathf.Clamp01((now - start) / duration);
                apply(curve(t));
                if (t < 1f) return;
                item?.Pause();
                if (ReferenceEquals(_item, item)) _item = null;
                done?.Invoke();
            }).Every(0);
            _item = item;
        }
    }

    /// <summary>图标：白色小图，样式里按名字（.ic-名字）贴图，再用 USS 着色。</summary>
    public static class UiIcon
    {
        public static VisualElement Create(string name, string extraClass = null)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ic");
            icon.AddToClassList("ic-" + name);
            if (extraClass != null) icon.AddToClassList(extraClass);
            return icon;
        }
    }
}
