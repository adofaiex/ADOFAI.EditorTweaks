#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>
    /// 编辑器右侧的侧边面板（布局 SidePanel.uxml，样式 Styles/SidePanel.uss）。
    /// 平时只在屏幕右边缘露出一个把手（左半边圆角矩形 + 小三角），点一下整个面板从右边滑出来，再点收回去。
    /// 面板 16:9、半透明底，左边是分类栏（现在只有"多人制谱"，以后的功能往这里加），右边是设置行（和设置窗口共用行组件）。
    /// </summary>
    public sealed class SidePanel
    {
        private const float CategoryStride = 68f;

        private readonly List<SettingsCategory> _categories;
        private readonly VisualElement _dock;
        private readonly VisualElement _handle;
        private readonly VisualElement _highlight;
        private readonly VisualElement _page;
        private readonly ScrollView _scroll;
        private readonly VisualElement _chip;
        private readonly Label _chipLabel;
        private readonly VisualElement _armA;
        private readonly VisualElement _armB;
        private readonly AvatarStack _avatars;
        private VisualElement _customContent;
        private readonly List<VisualElement> _categoryButtons = new List<VisualElement>();
        private readonly List<SettingRow> _rows = new List<SettingRow>();
        private readonly List<KeyValuePair<VisualElement, SettingsSection>> _headers = new List<KeyValuePair<VisualElement, SettingsSection>>();

        private int _category = -1;
        private int _token;
        private bool _open;
        private bool _available;
        private float _nextRefresh;
        private float _nextBlink;
        private bool _blinkOn;
        private string _lastStatus;
        private SettingRow _selected;

        /// <param name="host">挂载点：UIDocument 的根元素。</param>
        /// <param name="layout">SidePanel.uxml。</param>
        public SidePanel(VisualElement host, VisualTreeAsset layout, IEnumerable<SettingsCategory> categories)
        {
            _categories = new List<SettingsCategory>(categories);

            VisualElement root = layout.Instantiate();
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;
            root.pickingMode = PickingMode.Ignore;
            host.Add(root);

            _dock = Need<VisualElement>(root, "sp-dock");
            _handle = Need<VisualElement>(root, "sp-handle");
            _highlight = Need<VisualElement>(root, "sp-cat-hl");
            _page = Need<VisualElement>(root, "sp-page");
            _scroll = Need<ScrollView>(root, "sp-scroll");
            _chip = Need<VisualElement>(root, "sp-chip");
            _chipLabel = Need<Label>(root, "sp-chip-label");
            _armA = Need<VisualElement>(root, "sp-arm-a");
            _armB = Need<VisualElement>(root, "sp-arm-b");
            Tooltip = new UiTooltip(Need<VisualElement>(root, "sp-root"));
            _avatars = new AvatarStack(Tooltip);
            Need<VisualElement>(root, "sp-avatars").Add(_avatars);

            _handle.RegisterCallback<ClickEvent>(_ => Toggle());
            Need<VisualElement>(root, "sp-close").RegisterCallback<ClickEvent>(_ => Close());

            VisualElement cats = Need<VisualElement>(root, "sp-cats");
            for (int i = 0; i < _categories.Count; i++)
            {
                int index = i;
                var button = new VisualElement();
                button.AddToClassList("cat");
                button.Add(UiIcon.Create(_categories[i].Icon));
                var name = new Label(_categories[i].Name) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("cat-name");
                button.Add(name);
                button.RegisterCallback<ClickEvent>(_ => ShowCategory(index, animate: true));
                cats.Add(button);
                _categoryButtons.Add(button);
            }

            foreach (SettingsCategory category in _categories)
            {
                foreach (SettingsSection section in category.Sections)
                {
                    foreach (SettingRow row in section.Rows)
                    {
                        SettingRow captured = row;
                        _rows.Add(row);
                        row.Changed = RefreshDependencies;
                        row.Element.RegisterCallback<PointerEnterEvent>(_ => Select(captured));
                        row.Element.RegisterCallback<PointerLeaveEvent>(_ =>
                        {
                            if (_selected == captured) Select(null);
                        });
                    }
                }
            }

            root.schedule.Execute(OnTick).Every(16);
            ShowCategory(0, animate: false);
        }

        public bool IsOpen => _open;

        /// <summary>面板共用的悬停提示。</summary>
        public UiTooltip Tooltip { get; }

        /// <summary>顶栏右边显示的参与者头像。</summary>
        public Func<IReadOnlyList<PersonInfo>> People { get; set; }

        /// <summary>顶栏右边状态牌上的文字。</summary>
        public Func<string> StatusText { get; set; }

        /// <summary>状态牌的颜色："good" 绿、"busy" 黄、"warn" 红，null 灰。</summary>
        public Func<string> StatusTone { get; set; }

        /// <summary>为真时把手上亮小绿点（比如在多人房间里）。</summary>
        public Func<bool> Live { get; set; }

        /// <summary>为真时把手闪红，提醒用户点开（比如客人要选谱面放哪）。</summary>
        public Func<bool> Attention { get; set; }

        /// <summary>显示还是整个藏起来（不在编辑器里就藏起来，连把手一起）。</summary>
        public void SetAvailable(bool available)
        {
            if (available == _available) return;
            _available = available;
            _dock.EnableInClassList("gone", !available);
            if (!available) Close();
        }

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        public void Open()
        {
            if (_open || !_available) return;
            _open = true;
            _dock.AddToClassList("open");
            foreach (SettingRow row in _rows) row.Refresh(animate: false);
            RefreshDependencies();
            Populate(++_token);
        }

        /// <summary>打开并切到指定名字的分类。</summary>
        public void Open(string categoryName)
        {
            int index = _categories.FindIndex(c => c.Name == categoryName);
            if (index >= 0) ShowCategory(index, animate: _open);
            Open();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _dock.RemoveFromClassList("open");
            Select(null);
        }

        // ---------------------------------------------------------------- 分类与内容

        private void ShowCategory(int index, bool animate)
        {
            index = Mathf.Clamp(index, 0, _categories.Count - 1);
            if (index == _category) return;
            _category = index;
            int token = ++_token;
            for (int i = 0; i < _categoryButtons.Count; i++) _categoryButtons[i].EnableInClassList("selected", i == index);
            _highlight.style.top = index * CategoryStride;

            if (!animate)
            {
                Populate(token);
                return;
            }

            _page.AddToClassList("out");
            _page.schedule.Execute(() =>
            {
                if (token != _token) return;
                Populate(token);
                _page.RemoveFromClassList("out");
            }).StartingIn(130);
        }

        private void Populate(int token)
        {
            _scroll.Clear();
            _scroll.scrollOffset = Vector2.zero;
            _headers.Clear();
            int order = 0;
            SettingsCategory category = _categories[_category];
            category.Refresh?.Invoke();

            // 整页自定义内容（修改记录）：不显示设置行
            _customContent?.RemoveFromHierarchy();
            _customContent = category.Content;
            _scroll.style.display = _customContent == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (_customContent != null)
            {
                _page.Add(_customContent);
                FadeIn(_customContent, 0, token);
                return;
            }

            if (category.Header != null)
            {
                _scroll.Add(category.Header);
                FadeIn(category.Header, order++, token);
            }

            foreach (SettingsSection section in category.Sections)
            {
                var header = new Label(section.Title) { pickingMode = PickingMode.Ignore };
                header.AddToClassList("st-section");
                _scroll.Add(header);
                _headers.Add(new KeyValuePair<VisualElement, SettingsSection>(header, section));
                FadeIn(header, order++, token);
                foreach (SettingRow row in section.Rows)
                {
                    _scroll.Add(row.Element);
                    if (!row.IsCollapsed) FadeIn(row.Element, order++, token);
                }
            }

            UpdateSectionHeaders();
        }

        private void FadeIn(VisualElement element, int order, int token)
        {
            element.AddToClassList("hidden");
            element.schedule.Execute(() =>
            {
                if (token == _token) element.RemoveFromClassList("hidden");
            }).StartingIn(120 + order * 40);
        }

        private void Select(SettingRow row)
        {
            _selected = row;
            foreach (SettingRow other in _rows) other.SetSelected(other == row);
        }

        private void RefreshDependencies()
        {
            foreach (SettingRow row in _rows) row.SetCollapsed(!row.IsAvailable);
            UpdateSectionHeaders();
        }

        private void UpdateSectionHeaders()
        {
            foreach (KeyValuePair<VisualElement, SettingsSection> entry in _headers)
            {
                bool allCollapsed = true;
                foreach (SettingRow row in entry.Value.Rows)
                {
                    if (!row.IsCollapsed)
                    {
                        allCollapsed = false;
                        break;
                    }
                }

                entry.Key.EnableInClassList("collapsed", allCollapsed);
            }
        }

        // ---------------------------------------------------------------- 每帧

        private void OnTick()
        {
            float t = Time.realtimeSinceStartup;
            if (_open)
            {
                _armA.style.rotate = new Rotate(Angle.Degrees(t * 72f));
                _armB.style.rotate = new Rotate(Angle.Degrees(t * 72f + 180f));
            }

            if (t >= _nextRefresh)
            {
                _nextRefresh = t + 0.25f;
                RefreshStatus();
                if (_open)
                {
                    RefreshRows();
                    _categories[_category].Refresh?.Invoke();
                    if (People != null) _avatars.SetPeople(People());
                }
            }

            bool attention = !_open && Attention != null && Attention();
            if (!attention)
            {
                if (_blinkOn) _handle.RemoveFromClassList("attention");
                _blinkOn = false;
            }
            else if (t >= _nextBlink)
            {
                _nextBlink = t + 0.55f;
                _blinkOn = !_blinkOn;
                _handle.EnableInClassList("attention", _blinkOn);
            }
        }

        private void RefreshStatus()
        {
            string status = StatusText?.Invoke() ?? string.Empty;
            if (status != _lastStatus)
            {
                bool first = _lastStatus == null;
                _lastStatus = status;
                _chipLabel.text = status;
                _chip.style.display = status.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                if (!first)
                {
                    // 状态变了：小圆点跳一下
                    _chip.AddToClassList("beat");
                    _chip.schedule.Execute(() => _chip.RemoveFromClassList("beat")).StartingIn(260);
                }
            }

            string tone = StatusTone?.Invoke();
            _chip.EnableInClassList("good", tone == "good");
            _chip.EnableInClassList("busy", tone == "busy");
            _chip.EnableInClassList("warn", tone == "warn");
            _handle.EnableInClassList("live", Live != null && Live());
        }

        /// <summary>状态类的行自己刷新文字；可用与否变了就展开 / 收起。</summary>
        private void RefreshRows()
        {
            bool changed = false;
            foreach (SettingsSection section in _categories[_category].Sections)
            {
                foreach (SettingRow row in section.Rows)
                {
                    if (row.IsLive) row.Refresh(animate: true);
                    if (row.IsCollapsed == row.IsAvailable) changed = true;
                }
            }

            if (changed) RefreshDependencies();
        }

        private static T Need<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException("侧边面板布局（SidePanel.uxml）里找不到名为 " + name + " 的元素");
            return element;
        }
    }
}
