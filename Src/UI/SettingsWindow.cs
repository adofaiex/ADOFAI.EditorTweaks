#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>
    /// 设置窗口（风格照 ADOUAC 的设置页）：左边是分类栏（有一块会滑动的高亮），右边是当前分类的设置行，
    /// 底下是按键提示、“恢复默认”和白色的“关闭设置”。顶栏可以按住拖动窗口。
    /// 键盘：↑↓ 选行，←→ 调整，回车切换，Q / E（或 Tab）换分类，Esc 关闭。
    /// 样式全部在 USS 里（Assets/EditorTweaksUI/Styles），这里只管结构和动画的时序。
    /// 改动会在停手 0.7 秒后自动保存。
    /// </summary>
    public sealed class SettingsWindow
    {
        private const float CategoryStride = 68f;
        private const float WindowWidth = 1280f;
        private const float WindowHeight = 820f;
        private const float DragMargin = 120f;

        private readonly IEditorTweaksSettings _settings;
        private readonly List<SettingsCategory> _categories;
        private readonly Action _save;

        private readonly VisualElement _overlay;
        private readonly VisualElement _window;
        private readonly VisualElement _head;
        private readonly VisualElement _highlight;
        private readonly ScrollView _scroll;
        private readonly SmoothScroll _smooth;
        private readonly VisualElement _page;
        private readonly VisualElement _toast;
        private readonly Label _toastLabel;
        private readonly VisualElement _resetButton;
        private readonly VisualElement _resetIcon;
        private readonly Label _resetLabel;
        private readonly VisualElement _armA;
        private readonly VisualElement _armB;
        private readonly List<VisualElement> _categoryButtons = new List<VisualElement>();
        private readonly List<VisualElement> _intro = new List<VisualElement>();
        private readonly List<SettingRow> _navigation = new List<SettingRow>();
        private readonly List<KeyValuePair<VisualElement, SettingsSection>> _headers = new List<KeyValuePair<VisualElement, SettingsSection>>();
        private readonly Tweener _resetSpin;

        private int _category = -1;
        private int _selected = -1;
        private int _token;
        private int _pendingCategory = -1;
        private float _nextLiveRefresh;
        private bool _open;
        private bool _placed;
        private bool _dragging;
        private Vector2 _dragPointer;
        private Vector2 _dragOrigin;
        private bool _resetArmed;
        private bool _savePending;
        private IVisualElementScheduledItem _ticker;
        private IVisualElementScheduledItem _saveTimer;
        private IVisualElementScheduledItem _toastTimer;
        private IVisualElementScheduledItem _resetTimer;

        public bool IsOpen => _open;

        /// <summary>窗口关闭了（动画已开始，设置已保存）。</summary>
        public event Action Closed;

        /// <param name="host">挂载点：UIDocument 的根元素。</param>
        /// <param name="layout">静态外壳（Settings.uxml）。</param>
        /// <param name="save">把设置写到磁盘。</param>
        /// <param name="extraCategories">追加在后面的分类，可以为 null。</param>
        public SettingsWindow(VisualElement host, VisualTreeAsset layout, IEditorTweaksSettings settings, string version, Action save,
            IEnumerable<SettingsCategory> extraCategories = null)
        {
            _settings = settings;
            _save = save;
            _categories = SettingsSchema.Build(settings);
            if (extraCategories != null) _categories.AddRange(extraCategories);

            VisualElement root = layout.Instantiate();
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;
            root.pickingMode = PickingMode.Ignore;
            host.Add(root);

            _overlay = Need<VisualElement>(root, "overlay");
            _overlay.style.display = DisplayStyle.None;
            _window = Need<VisualElement>(root, "window");
            _head = Need<VisualElement>(root, "head");
            _armA = Need<VisualElement>(root, "arm-a");
            _armB = Need<VisualElement>(root, "arm-b");
            Need<Label>(root, "tag").text = "EDITOR TWEAKS  ·  v" + version;

            Need<VisualElement>(root, "x").RegisterCallback<ClickEvent>(_ => Close());
            _head.RegisterCallback<PointerDownEvent>(OnDragStart);
            _head.RegisterCallback<PointerMoveEvent>(OnDragMove);
            _head.RegisterCallback<PointerUpEvent>(OnDragEnd);
            _head.RegisterCallback<PointerCaptureOutEvent>(_ => _dragging = false);

            // ---- 分类按钮（按设置清单生成，放在会滑动的高亮后面）
            _highlight = Need<VisualElement>(root, "cat-hl");
            VisualElement cats = Need<VisualElement>(root, "cats");
            for (int i = 0; i < _categories.Count; i++)
            {
                int index = i;
                SettingsCategory category = _categories[i];
                var button = El("cat", "intro");
                button.Add(UiIcon.Create(category.Icon, "lg"));
                var name = new Label(category.Name) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("cat-name");
                button.Add(name);
                button.RegisterCallback<ClickEvent>(_ => ShowCategory(index, animate: true));
                cats.Add(button);
                _categoryButtons.Add(button);
            }

            _page = Need<VisualElement>(root, "page");
            _scroll = Need<ScrollView>(root, "scroll");
            _smooth = SmoothScroll.Attach(_scroll);

            // ---- 底栏
            _resetButton = Need<VisualElement>(root, "reset");
            _resetIcon = Need<VisualElement>(root, "reset-icon");
            _resetLabel = Need<Label>(root, "reset-label");
            _resetButton.RegisterCallback<ClickEvent>(_ => OnResetClicked());
            _resetSpin = new Tweener(_resetButton);
            Need<VisualElement>(root, "close").RegisterCallback<ClickEvent>(_ => Close());

            _toast = Need<VisualElement>(root, "toast");
            _toastLabel = Need<Label>(root, "toast-label");

            // 进场动画：带 intro 类的元素按文档顺序依次滑入
            _overlay.Query(className: "intro").ForEach(element => _intro.Add(element));

            // ---- 每一行：悬停选中、改动回调
            foreach (SettingsCategory category in _categories)
            {
                foreach (SettingsSection section in category.Sections)
                {
                    foreach (SettingRow row in section.Rows)
                    {
                        SettingRow captured = row;
                        row.Changed = OnRowChanged;
                        row.Element.RegisterCallback<PointerEnterEvent>(_ => SelectRow(captured));
                    }
                }
            }

            _overlay.RegisterCallback<GeometryChangedEvent>(_ => PlaceWindow());
            _overlay.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<NavigationMoveEvent>(OnNavigation, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<NavigationSubmitEvent>(OnNavigation, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<NavigationCancelEvent>(OnNavigation, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!(e.target is VisualElement target) || target.GetFirstAncestorOfType<TextField>() == null) _overlay.Focus();
            }, TrickleDown.TrickleDown);
        }

        // ---------------------------------------------------------------- 打开 / 关闭

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        /// <summary>打开窗口并切到指定名字的分类（已经开着就直接切过去）。</summary>
        public void Open(string categoryName)
        {
            int index = _categories.FindIndex(c => c.Name == categoryName);
            if (!_open)
            {
                _pendingCategory = index;
                Open();
                return;
            }

            if (index >= 0) ShowCategory(index, animate: true);
        }

        public void Open()
        {
            if (_open) return;
            _open = true;
            _category = -1;
            _selected = -1;
            _token++;

            foreach (SettingsCategory category in _categories)
                foreach (SettingsSection section in category.Sections)
                    foreach (SettingRow row in section.Rows)
                        row.Refresh(animate: false);
            RefreshDependencies();
            DisarmReset();

            _overlay.style.display = DisplayStyle.Flex;
            _overlay.RemoveFromClassList("shown");
            foreach (VisualElement element in _intro) element.AddToClassList("intro-hidden");
            ShowCategory(Mathf.Max(0, _pendingCategory), animate: false);
            _pendingCategory = -1;

            // 下一帧再加 shown，过渡才会播
            _overlay.schedule.Execute(() =>
            {
                if (!_open) return;
                PlaceWindow();
                _overlay.AddToClassList("shown");
                _overlay.Focus();
                for (int i = 0; i < _intro.Count; i++)
                {
                    VisualElement element = _intro[i];
                    int token = _token;
                    element.schedule.Execute(() =>
                    {
                        if (token == _token) element.RemoveFromClassList("intro-hidden");
                    }).StartingIn(140 + i * 42);
                }
            }).StartingIn(30);

            _ticker?.Pause();
            _ticker = _overlay.schedule.Execute(OnTick).Every(16);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _dragging = false;
            _token++;
            FlushSave();
            DisarmReset();
            _overlay.RemoveFromClassList("shown");
            int token = _token;
            _overlay.schedule.Execute(() =>
            {
                if (_open || token != _token) return;
                _overlay.style.display = DisplayStyle.None;
                _ticker?.Pause();
            }).StartingIn(380);
            Closed?.Invoke();
        }

        // ---------------------------------------------------------------- 拖动窗口

        /// <summary>第一次显示时放到屏幕正中；以后保持用户拖到的位置，屏幕大小变了就夹回可见范围。</summary>
        private void PlaceWindow()
        {
            float width = _overlay.resolvedStyle.width;
            float height = _overlay.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width < 1f || height < 1f) return;

            if (!_placed)
            {
                _placed = true;
                SetWindowPosition((width - WindowWidth) * 0.5f, (height - WindowHeight) * 0.5f, width, height);
                return;
            }

            SetWindowPosition(_window.resolvedStyle.left, _window.resolvedStyle.top, width, height);
        }

        private void SetWindowPosition(float left, float top, float overlayWidth, float overlayHeight)
        {
            // 至少留一截顶栏在屏幕里，免得拖丢了
            left = Mathf.Clamp(left, DragMargin - WindowWidth, overlayWidth - DragMargin);
            top = Mathf.Clamp(top, 0f, overlayHeight - 80f);
            _window.style.left = left;
            _window.style.top = top;
        }

        private void OnDragStart(PointerDownEvent e)
        {
            if (e.button != 0 || IsOnClassWithin(e.target, "etw-x")) return;
            _dragging = true;
            _dragPointer = e.position;
            _dragOrigin = new Vector2(_window.resolvedStyle.left, _window.resolvedStyle.top);
            _head.CapturePointer(e.pointerId);
            _window.AddToClassList("dragging");
            e.StopPropagation();
        }

        private void OnDragMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            Vector2 delta = (Vector2)e.position - _dragPointer;
            SetWindowPosition(_dragOrigin.x + delta.x, _dragOrigin.y + delta.y, _overlay.resolvedStyle.width, _overlay.resolvedStyle.height);
        }

        private void OnDragEnd(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false;
            _head.ReleasePointer(e.pointerId);
            _window.RemoveFromClassList("dragging");
        }

        private bool IsOnClassWithin(IEventHandler target, string className)
        {
            for (var element = target as VisualElement; element != null && element != _head; element = element.parent)
                if (element.ClassListContains(className)) return true;
            return false;
        }

        // ---------------------------------------------------------------- 分类

        private void ShowCategory(int index, bool animate)
        {
            index = Mathf.Clamp(index, 0, _categories.Count - 1);
            if (index == _category) return;
            bool first = _category < 0;
            _category = index;
            int token = ++_token;

            for (int i = 0; i < _categoryButtons.Count; i++) _categoryButtons[i].EnableInClassList("selected", i == index);
            _highlight.style.top = index * CategoryStride;

            if (first || !animate)
            {
                Populate(token);
                return;
            }

            // 先把旧内容淡出，再换成新分类，新的一行行滑进来
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
            foreach (SettingsSection section in _categories[_category].Sections)
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

            RebuildNavigation();
            UpdateSectionHeaders();
            _selected = -1;
            SelectIndex(0, scroll: false);
        }

        /// <summary>一个分区里的行全收起了，分区小标题也跟着收起。</summary>
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

        /// <summary>先藏起来，隔一点点时间再放出来（USS 里有透明度和位移的过渡）。</summary>
        private void FadeIn(VisualElement element, int order, int token)
        {
            element.AddToClassList("hidden");
            element.schedule.Execute(() =>
            {
                if (token == _token) element.RemoveFromClassList("hidden");
            }).StartingIn(40 + order * 38);
        }

        private void SwitchCategory(int direction)
        {
            int next = _category + direction;
            if (next < 0 || next >= _categories.Count) return;
            ShowCategory(next, animate: true);
        }

        // ---------------------------------------------------------------- 选行与键盘

        private void RebuildNavigation()
        {
            _navigation.Clear();
            foreach (SettingsSection section in _categories[_category].Sections)
                foreach (SettingRow row in section.Rows)
                    if (!row.IsCollapsed) _navigation.Add(row);
        }

        private void SelectRow(SettingRow row)
        {
            int index = _navigation.IndexOf(row);
            if (index >= 0 && index != _selected) SelectIndex(index, scroll: false);
        }

        private void SelectIndex(int index, bool scroll)
        {
            if (_navigation.Count == 0) return;
            _selected = Mathf.Clamp(index, 0, _navigation.Count - 1);
            for (int i = 0; i < _navigation.Count; i++) _navigation[i].SetSelected(i == _selected);
            if (scroll) _smooth.ScrollIntoView(_navigation[_selected].Element);
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (!_open) return;
            if (IsTyping(e.target)) return;

            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    break;
                case KeyCode.UpArrow:
                case KeyCode.W:
                    SelectIndex(_selected - 1, scroll: true);
                    break;
                case KeyCode.DownArrow:
                case KeyCode.S:
                    SelectIndex(_selected + 1, scroll: true);
                    break;
                case KeyCode.LeftArrow:
                case KeyCode.A:
                    Current()?.Nudge(-1);
                    break;
                case KeyCode.RightArrow:
                case KeyCode.D:
                    Current()?.Nudge(1);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    Current()?.Activate();
                    break;
                case KeyCode.Q:
                case KeyCode.PageUp:
                    SwitchCategory(-1);
                    break;
                case KeyCode.E:
                case KeyCode.PageDown:
                    SwitchCategory(1);
                    break;
                case KeyCode.Tab:
                    SwitchCategory(e.shiftKey ? -1 : 1);
                    break;
                default:
                    return;
            }

            e.StopPropagation();
        }

        /// <summary>UI Toolkit 自带的方向键 / 回车导航会把焦点挪到输入框里，这里拦掉（在输入框里时除外）。</summary>
        private void OnNavigation(EventBase e)
        {
            if (!_open || IsTyping(e.target)) return;
            e.StopPropagation();
            _overlay.focusController?.IgnoreEvent(e);
        }

        private static bool IsTyping(IEventHandler target)
        {
            return target is VisualElement element && (element is TextField || element.GetFirstAncestorOfType<TextField>() != null);
        }

        private SettingRow Current()
        {
            return _selected >= 0 && _selected < _navigation.Count ? _navigation[_selected] : null;
        }

        // ---------------------------------------------------------------- 改动之后

        private void OnRowChanged()
        {
            RefreshDependencies();
            QueueSave();
        }

        /// <summary>依赖别的开关的行：开关关了就收起来，开了再展开。</summary>
        private void RefreshDependencies()
        {
            foreach (SettingsCategory category in _categories)
                foreach (SettingsSection section in category.Sections)
                    foreach (SettingRow row in section.Rows)
                        row.SetCollapsed(!row.IsAvailable);
            UpdateSectionHeaders();
            if (_category >= 0)
            {
                SettingRow before = Current();
                RebuildNavigation();
                int index = before == null ? -1 : _navigation.IndexOf(before);
                if (index >= 0) _selected = index;
                else SelectIndex(Mathf.Min(_selected, _navigation.Count - 1), scroll: false);
            }
        }

        private void QueueSave()
        {
            _savePending = true;
            _saveTimer?.Pause();
            _saveTimer = _overlay.schedule.Execute(() =>
            {
                FlushSave();
                ShowToast("已自动保存");
            }).StartingIn(700);
        }

        private void FlushSave()
        {
            _saveTimer?.Pause();
            if (!_savePending) return;
            _savePending = false;
            _save?.Invoke();
        }

        private void ShowToast(string text)
        {
            if (!_open) return;
            _toastLabel.text = text;
            _toast.AddToClassList("show");
            _toastTimer?.Pause();
            _toastTimer = _toast.schedule.Execute(() => _toast.RemoveFromClassList("show")).StartingIn(1800);
        }

        // ---------------------------------------------------------------- 恢复默认（点两下确认）

        private void OnResetClicked()
        {
            if (!_resetArmed)
            {
                _resetArmed = true;
                _resetButton.AddToClassList("armed");
                _resetLabel.text = "再点一次确认";
                _resetTimer?.Pause();
                _resetTimer = _resetButton.schedule.Execute(DisarmReset).StartingIn(2600);
                return;
            }

            DisarmReset();
            _settings.ResetToDefaults();
            foreach (SettingsCategory category in _categories)
                foreach (SettingsSection section in category.Sections)
                    foreach (SettingRow row in section.Rows)
                        row.Refresh(animate: true);
            RefreshDependencies();
            _savePending = true;
            FlushSave();
            ShowToast("已恢复默认设置");
            _resetSpin.Run(0.8f, t => _resetIcon.style.rotate = new Rotate(Angle.Degrees(-360f * t)), UiEase.OutBack);
        }

        private void DisarmReset()
        {
            _resetTimer?.Pause();
            if (!_resetArmed && _resetLabel.text == "恢复默认") return;
            _resetArmed = false;
            _resetButton.RemoveFromClassList("armed");
            _resetLabel.text = "恢复默认";
        }

        // ---------------------------------------------------------------- 每帧：Logo 公转

        private void OnTick()
        {
            float t = Time.realtimeSinceStartup;
            if (t >= _nextLiveRefresh)
            {
                _nextLiveRefresh = t + 0.25f;
                RefreshLiveRows();
            }

            _armA.style.rotate = new Rotate(Angle.Degrees(t * 72f));
            _armB.style.rotate = new Rotate(Angle.Degrees(t * 72f + 180f));
        }

        /// <summary>状态类的行自己刷新文字；可用与否变了就展开 / 收起。</summary>
        private void RefreshLiveRows()
        {
            if (_category < 0) return;
            bool availabilityChanged = false;
            foreach (SettingsSection section in _categories[_category].Sections)
            {
                foreach (SettingRow row in section.Rows)
                {
                    if (row.IsLive) row.Refresh(animate: true);
                    if (row.IsCollapsed == row.IsAvailable) availabilityChanged = true;
                }
            }

            if (availabilityChanged) RefreshDependencies();
        }

        // ---------------------------------------------------------------- 小工具

        private static T Need<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException("设置界面布局（Settings.uxml）里找不到名为 " + name + " 的元素");
            return element;
        }

        private static VisualElement El(params string[] classes)
        {
            var element = new VisualElement();
            foreach (string name in classes) element.AddToClassList(name);
            return element;
        }
    }
}
