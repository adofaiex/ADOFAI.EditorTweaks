#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>头像栏里的一个人。</summary>
    public sealed class PersonInfo
    {
        public ulong Key;
        public string Name;
        public Texture2D Avatar;

        /// <summary>小标签（"房主" / "你"），可以为空。</summary>
        public string Badge;

        public bool IsHost;

        /// <summary>正在说话（头像亮绿圈）。</summary>
        public bool Speaking;

        /// <summary>被我静音了（头像上盖一个静音图标）。</summary>
        public bool Muted;

        /// <summary>点头像做什么（比如静音 / 取消静音），null 表示不能点。</summary>
        public Action Click;
    }

    /// <summary>当前谱面卡片显示的内容。</summary>
    public sealed class LevelCardInfo
    {
        public string Title;
        public string Subtitle;
        public string Path;
        public Texture2D Preview;
        public readonly List<KeyValuePair<string, string>> Stats = new List<KeyValuePair<string, string>>();
    }

    /// <summary>修改记录里的一条。</summary>
    public sealed class ActivityItem
    {
        public long Id;
        public DateTime Time;
        public string Icon;
        public string Who;
        public Texture2D Avatar;
        public string Text;

        /// <summary>点一下跳到相关的位置；没有就是 null。</summary>
        public Action Jump;
    }

    /// <summary>
    /// 鼠标悬停提示：整个面板共用一个小黑条，悬停在目标上时淡入到目标下方。
    /// UI Toolkit 运行时没有自带 tooltip，这里自己画。
    /// </summary>
    public sealed class UiTooltip
    {
        private readonly VisualElement _layer;
        private readonly Label _label;
        private VisualElement _target;

        public UiTooltip(VisualElement layer)
        {
            _layer = layer;
            _label = new Label { pickingMode = PickingMode.Ignore };
            _label.AddToClassList("tip");
            layer.Add(_label);
            // 量出提示条的宽度后，别让它伸出屏幕左右边缘
            _label.RegisterCallback<GeometryChangedEvent>(_ => Clamp());
        }

        private float _anchorX;

        private void Clamp()
        {
            float width = _label.resolvedStyle.width;
            float layerWidth = _layer.resolvedStyle.width;
            if (float.IsNaN(width) || float.IsNaN(layerWidth) || width <= 0f) return;
            float x = Mathf.Clamp(_anchorX, width * 0.5f + 8f, Mathf.Max(width * 0.5f + 8f, layerWidth - width * 0.5f - 8f));
            if (Mathf.Abs(_label.resolvedStyle.left - x) > 0.5f) _label.style.left = x;
        }

        /// <summary>悬停期间内容变了（比如延迟数字）：更新提示文字。</summary>
        public void Update(VisualElement target, string text)
        {
            if (_target != target || string.IsNullOrEmpty(text) || _label.text == text) return;
            _label.text = text;
        }

        public void Attach(VisualElement target, Func<string> text)
        {
            target.RegisterCallback<PointerEnterEvent>(_ => Show(target, text()));
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide(target));
            target.RegisterCallback<DetachFromPanelEvent>(_ => Hide(target));
        }

        private void Show(VisualElement target, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _target = target;
            _label.text = text;
            Rect bounds = _layer.WorldToLocal(target.worldBound);
            _anchorX = bounds.center.x;
            _label.style.left = bounds.center.x;
            Clamp();
            _label.style.top = bounds.yMax + 10f;
            _label.BringToFront();
            _label.AddToClassList("show");
        }

        private void Hide(VisualElement target)
        {
            if (_target != target) return;
            _target = null;
            _label.RemoveFromClassList("show");
        }
    }

    /// <summary>参与者头像（叠在一起的圆形头像，房主红框；新进来的弹出来，离开的缩小消失）。</summary>
    public sealed class AvatarStack : VisualElement
    {
        private readonly UiTooltip _tooltip;
        private readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();

        public AvatarStack(UiTooltip tooltip)
        {
            _tooltip = tooltip;
            AddToClassList("av-stack");
            pickingMode = PickingMode.Ignore;
        }

        public void SetPeople(IReadOnlyList<PersonInfo> people)
        {
            var keep = new HashSet<ulong>();
            for (int i = 0; i < people.Count; i++)
            {
                PersonInfo person = people[i];
                keep.Add(person.Key);
                if (!_entries.TryGetValue(person.Key, out Entry entry))
                {
                    entry = new Entry(person, _tooltip);
                    _entries[person.Key] = entry;
                    Add(entry.Element);
                    VisualElement element = entry.Element;
                    element.schedule.Execute(() => element.AddToClassList("in")).StartingIn(30 + i * 60);
                }

                entry.Update(person);
                if (IndexOf(entry.Element) != i && i < childCount) entry.Element.PlaceBehind(ElementAt(i));
            }

            foreach (ulong key in new List<ulong>(_entries.Keys))
            {
                if (keep.Contains(key)) continue;
                VisualElement element = _entries[key].Element;
                _entries.Remove(key);
                element.RemoveFromClassList("in");
                element.schedule.Execute(() => element.RemoveFromHierarchy()).StartingIn(320);
            }
        }

        private sealed class Entry
        {
            private readonly Label _initial;
            private readonly Label _badge;
            private readonly VisualElement _muteIcon;
            private PersonInfo _person;

            public Entry(PersonInfo person, UiTooltip tooltip)
            {
                _person = person;
                Element = new VisualElement();
                Element.AddToClassList("av");
                _initial = new Label { pickingMode = PickingMode.Ignore };
                _initial.AddToClassList("av-initial");
                Element.Add(_initial);
                _badge = new Label { pickingMode = PickingMode.Ignore };
                _badge.AddToClassList("av-badge");
                Element.Add(_badge);
                _muteIcon = UiIcon.Create("mic-off", "sm av-mute");
                Element.Add(_muteIcon);
                Element.RegisterCallback<ClickEvent>(_ => _person.Click?.Invoke());
                tooltip.Attach(Element, () =>
                {
                    string text = string.IsNullOrEmpty(_person.Badge) ? _person.Name : _person.Name + "（" + _person.Badge + "）";
                    if (_person.Click != null) text += _person.Muted ? " · 已静音，点击取消" : " · 点击静音";
                    return text;
                });
            }

            public VisualElement Element { get; }

            public void Update(PersonInfo person)
            {
                _person = person;
                Element.EnableInClassList("host", person.IsHost);
                Element.EnableInClassList("speaking", person.Speaking && !person.Muted);
                Element.EnableInClassList("muted", person.Muted);
                Element.EnableInClassList("clickable", person.Click != null);
                if (person.Avatar != null)
                {
                    Element.style.backgroundImage = new StyleBackground(person.Avatar);
                    _initial.text = string.Empty;
                }
                else
                {
                    Element.style.backgroundImage = StyleKeyword.None;
                    _initial.text = string.IsNullOrEmpty(person.Name) ? "?" : person.Name.Substring(0, 1).ToUpperInvariant();
                }

                _badge.text = person.IsHost ? "房主" : string.Empty;
                _badge.style.display = person.IsHost ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }

    /// <summary>当前谱面卡片：左边封面（谱面的预览图，没有就是音符图标），右边曲名、作者、几个数字和文件路径。</summary>
    public sealed class LevelCard : VisualElement
    {
        private readonly VisualElement _cover;
        private readonly VisualElement _coverIcon;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly VisualElement _stats;
        private readonly Label _path;
        private readonly Dictionary<string, Label> _statValues = new Dictionary<string, Label>();
        private Texture2D _shownPreview;

        public LevelCard()
        {
            AddToClassList("lv-card");
            _cover = new VisualElement { pickingMode = PickingMode.Ignore };
            _cover.AddToClassList("lv-cover");
            _coverIcon = UiIcon.Create("music", "lv-cover-icon");
            _cover.Add(_coverIcon);
            Add(_cover);

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("lv-text");
            _title = new Label { pickingMode = PickingMode.Ignore };
            _title.AddToClassList("lv-title");
            _subtitle = new Label { pickingMode = PickingMode.Ignore };
            _subtitle.AddToClassList("lv-subtitle");
            _stats = new VisualElement { pickingMode = PickingMode.Ignore };
            _stats.AddToClassList("lv-stats");
            _path = new Label { pickingMode = PickingMode.Ignore };
            _path.AddToClassList("lv-path");
            text.Add(_title);
            text.Add(_subtitle);
            text.Add(_stats);
            text.Add(_path);
            Add(text);
        }

        public void SetInfo(LevelCardInfo info)
        {
            if (info == null) return;
            SetText(_title, info.Title);
            SetText(_subtitle, info.Subtitle);
            SetText(_path, info.Path);
            if (info.Preview != _shownPreview)
            {
                _shownPreview = info.Preview;
                _cover.style.backgroundImage = info.Preview != null ? new StyleBackground(info.Preview) : new StyleBackground(StyleKeyword.None);
                _coverIcon.style.display = info.Preview != null ? DisplayStyle.None : DisplayStyle.Flex;
                // 换封面时闪一下
                _cover.AddToClassList("flash");
                _cover.schedule.Execute(() => _cover.RemoveFromClassList("flash")).StartingIn(60);
            }

            var seen = new HashSet<string>();
            foreach (KeyValuePair<string, string> stat in info.Stats)
            {
                seen.Add(stat.Key);
                if (!_statValues.TryGetValue(stat.Key, out Label value))
                {
                    var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                    chip.AddToClassList("lv-stat");
                    value = new Label { pickingMode = PickingMode.Ignore };
                    value.AddToClassList("lv-stat-value");
                    var name = new Label(stat.Key) { pickingMode = PickingMode.Ignore };
                    name.AddToClassList("lv-stat-name");
                    chip.Add(value);
                    chip.Add(name);
                    _stats.Add(chip);
                    _statValues[stat.Key] = value;
                }

                if (value.text != stat.Value)
                {
                    bool first = string.IsNullOrEmpty(value.text);
                    value.text = stat.Value;
                    if (!first)
                    {
                        // 数字变了：跳一下
                        value.AddToClassList("bump");
                        Label captured = value;
                        value.schedule.Execute(() => captured.RemoveFromClassList("bump")).StartingIn(180);
                    }
                }

                value.parent.style.display = DisplayStyle.Flex;
            }

            foreach (KeyValuePair<string, Label> pair in _statValues)
                if (!seen.Contains(pair.Key)) pair.Value.parent.style.display = DisplayStyle.None;
        }

        private static void SetText(Label label, string text)
        {
            text = text ?? string.Empty;
            if (label.text != text) label.text = text;
            label.style.display = text.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    /// <summary>修改记录页：最新的在最上面，新来的从上面滑进来；每条有人、图标、说明和"几分钟前"，能跳转的点一下就跳过去。</summary>
    public sealed class ActivityFeed : VisualElement
    {
        private const int MaxShown = 150;

        private readonly ScrollView _scroll;
        private readonly VisualElement _empty;
        private readonly Func<IReadOnlyList<ActivityItem>> _source;
        private readonly List<KeyValuePair<ActivityItem, Label>> _times = new List<KeyValuePair<ActivityItem, Label>>();
        private long _newest = -1;

        public ActivityFeed(Func<IReadOnlyList<ActivityItem>> source)
        {
            _source = source;
            AddToClassList("feed");
            _scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _scroll.AddToClassList("etw-scroll");
            _scroll.AddToClassList("feed-scroll");
            Add(_scroll);
            SmoothScroll.Attach(_scroll);

            _empty = new VisualElement { pickingMode = PickingMode.Ignore };
            _empty.AddToClassList("feed-empty");
            _empty.Add(UiIcon.Create("history", "lg"));
            var text = new Label("还没有修改记录") { pickingMode = PickingMode.Ignore };
            text.AddToClassList("feed-empty-title");
            var hint = new Label("进入房间后，大家的每一次修改、加入和离开都会记在这里；点一条能跳到对应的砖块或装饰。") { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("feed-empty-hint");
            _empty.Add(text);
            _empty.Add(hint);
            Add(_empty);
        }

        /// <summary>取最新的记录，把没显示过的加到最上面；顺便刷新"几分钟前"。</summary>
        public void Refresh()
        {
            IReadOnlyList<ActivityItem> items = _source();
            _empty.style.display = items.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (items.Count > 0 && items[items.Count - 1].Id < _newest)
            {
                // 记录被清空过（换了房间）
                _scroll.Clear();
                _times.Clear();
                _newest = -1;
            }

            int order = 0;
            foreach (ActivityItem item in items)
            {
                if (item.Id <= _newest) continue;
                VisualElement row = Build(item);
                _scroll.Insert(0, row);
                row.AddToClassList("enter");
                row.schedule.Execute(() => row.RemoveFromClassList("enter")).StartingIn(30 + order++ * 25);
                _newest = item.Id;
            }

            if (items.Count == 0 && _scroll.childCount > 0)
            {
                _scroll.Clear();
                _times.Clear();
                _newest = -1;
            }

            while (_scroll.childCount > MaxShown) _scroll.RemoveAt(_scroll.childCount - 1);
            if (_times.Count > MaxShown) _times.RemoveRange(0, _times.Count - MaxShown);
            foreach (KeyValuePair<ActivityItem, Label> pair in _times)
            {
                string text = Ago(pair.Key.Time);
                if (pair.Value.text != text) pair.Value.text = text;
            }
        }

        private VisualElement Build(ActivityItem item)
        {
            var row = new VisualElement();
            row.AddToClassList("feed-row");
            if (item.Jump != null)
            {
                row.AddToClassList("link");
                row.RegisterCallback<ClickEvent>(_ => item.Jump());
            }

            var avatar = new VisualElement { pickingMode = PickingMode.Ignore };
            avatar.AddToClassList("feed-av");
            if (item.Avatar != null) avatar.style.backgroundImage = new StyleBackground(item.Avatar);
            else
            {
                var initial = new Label(string.IsNullOrEmpty(item.Who) ? "·" : item.Who.Substring(0, 1).ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                initial.AddToClassList("feed-av-initial");
                avatar.Add(initial);
            }

            row.Add(avatar);
            row.Add(UiIcon.Create(item.Icon ?? "info", "sm feed-icon"));

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("feed-text");
            var who = new Label(item.Who ?? string.Empty) { pickingMode = PickingMode.Ignore };
            who.AddToClassList("feed-who");
            var what = new Label(item.Text ?? string.Empty) { pickingMode = PickingMode.Ignore };
            what.AddToClassList("feed-what");
            text.Add(who);
            text.Add(what);
            row.Add(text);

            var time = new Label(Ago(item.Time)) { pickingMode = PickingMode.Ignore };
            time.AddToClassList("feed-time");
            row.Add(time);
            _times.Add(new KeyValuePair<ActivityItem, Label>(item, time));
            if (item.Jump != null) row.Add(UiIcon.Create("chevron-right", "sm feed-go"));
            return row;
        }

        private static string Ago(DateTime time)
        {
            TimeSpan span = DateTime.Now - time;
            if (span.TotalSeconds < 10) return "刚刚";
            if (span.TotalMinutes < 1) return (int)span.TotalSeconds + " 秒前";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " 分钟前";
            return time.ToString("HH:mm");
        }
    }

    /// <summary>右上角延迟牌显示的内容。</summary>
    public sealed class PingInfo
    {
        /// <summary>延迟（毫秒）；房主时是到各个客人里最慢的那个。</summary>
        public int Ms;

        /// <summary>丢包率（0~1）。</summary>
        public float Loss;

        /// <summary>悬停时显示的详细情况（每人一行）。</summary>
        public string Detail;
    }

    /// <summary>
    /// 屏幕右上角的延迟小牌子：三格信号 + 平滑后的毫秒数（绿 &lt;80、黄 &lt;160、红），有丢包时加一行丢包率；
    /// 悬停显示每个人的延迟。数字按指数平滑，不会一跳一跳。
    /// </summary>
    public sealed class PingBadge : VisualElement
    {
        private readonly VisualElement[] _bars = new VisualElement[3];
        private readonly Label _value;
        private readonly Label _loss;
        private readonly UiTooltip _tooltip;
        private float _shown = -1f;
        private string _detail;

        public PingBadge(UiTooltip tooltip)
        {
            _tooltip = tooltip;
            AddToClassList("ping");
            var bars = new VisualElement { pickingMode = PickingMode.Ignore };
            bars.AddToClassList("ping-bars");
            for (int i = 0; i < 3; i++)
            {
                _bars[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                _bars[i].AddToClassList("ping-bar");
                _bars[i].AddToClassList("b" + i);
                bars.Add(_bars[i]);
            }

            Add(bars);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("ping-text");
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("ping-row");
            _value = new Label("--") { pickingMode = PickingMode.Ignore };
            _value.AddToClassList("ping-value");
            var unit = new Label("ms") { pickingMode = PickingMode.Ignore };
            unit.AddToClassList("ping-unit");
            row.Add(_value);
            row.Add(unit);
            text.Add(row);
            _loss = new Label { pickingMode = PickingMode.Ignore };
            _loss.AddToClassList("ping-loss");
            text.Add(_loss);
            Add(text);
            tooltip.Attach(this, () => _detail);
        }

        /// <summary>显示 / 藏起来（藏的时候淡出并往上收）。</summary>
        public void SetVisible(bool visible)
        {
            EnableInClassList("show", visible);
            if (!visible) _shown = -1f;
        }

        public void SetInfo(PingInfo info)
        {
            if (info == null) return;
            float target = Mathf.Max(0, info.Ms);
            _shown = _shown < 0f ? target : Mathf.Lerp(_shown, target, 0.35f);
            int ms = Mathf.RoundToInt(_shown);
            string text = ms.ToString();
            if (_value.text != text) _value.text = text;

            int level = ms < 80 ? 3 : ms < 160 ? 2 : 1;
            if (info.Loss > 0.1f) level = Mathf.Min(level, 1);
            else if (info.Loss > 0.02f) level = Mathf.Min(level, 2);
            EnableInClassList("good", level == 3);
            EnableInClassList("fair", level == 2);
            EnableInClassList("poor", level == 1);
            for (int i = 0; i < 3; i++) _bars[i].EnableInClassList("lit", i < level);

            bool lossy = info.Loss >= 0.01f;
            _loss.text = lossy ? "丢包 " + Mathf.RoundToInt(info.Loss * 100f) + "%" : string.Empty;
            _loss.style.display = lossy ? DisplayStyle.Flex : DisplayStyle.None;
            _detail = info.Detail;
            _tooltip.Update(this, _detail);
        }
    }
}
