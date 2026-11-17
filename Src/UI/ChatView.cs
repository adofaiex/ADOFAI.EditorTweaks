#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    public enum ChatLineKind
    {
        Other,
        Mine,
        System,
    }

    /// <summary>聊天里的一行。</summary>
    public sealed class ChatLine
    {
        public long Id;
        public DateTime Time;
        public ChatLineKind Kind;
        public ulong SenderKey;
        public string Sender;
        public Texture2D Avatar;
        public bool IsHost;
        public string Text;
    }

    /// <summary>聊天页用到的数据和操作（界面库不依赖多人模块，由 Mod 那边提供）。</summary>
    public sealed class ChatSource
    {
        public Func<IReadOnlyList<ChatLine>> Lines;
        public Action<string> Send;
        public Func<bool> CanSend;
        public Func<string> Placeholder;
        public int MaxLength = 500;

        public Func<bool> MicOn;
        public Action<bool> SetMic;
        public Func<bool> CanTalk;
        public Func<bool> Speaking;
        public Func<string> VoiceStatus;
        public Func<float> Volume;
        public Action<float> SetVolume;
    }

    /// <summary>
    /// 聊天页：上面是语音条（麦克风开关、状态、别人的音量），中间是消息（自己的靠右、别人的靠左、系统提示居中；
    /// 同一个人连续说的话合在一起），下面是输入框和发送按钮（回车发送）。新消息滑进来，停在底部时自动跟到最新。
    /// </summary>
    public sealed class ChatView : VisualElement
    {
        private readonly ChatSource _source;
        private readonly ScrollView _scroll;
        private readonly SmoothScroll _smooth;
        private readonly TextField _field;
        private readonly VisualElement _send;
        private readonly VisualElement _mic;
        private readonly VisualElement _micIcon;
        private readonly Label _micTitle;
        private readonly Label _voiceStatus;
        private readonly AnimatedSlider _volume;
        private readonly Label _volumeValue;
        private readonly VisualElement _empty;
        private long _newest = -1;
        private ChatLine _previous;

        public ChatView(ChatSource source)
        {
            _source = source;
            AddToClassList("chat");

            // ---- 语音条
            var voice = new VisualElement();
            voice.AddToClassList("chat-voice");
            _mic = new VisualElement();
            _mic.AddToClassList("chat-mic");
            _micIcon = UiIcon.Create("mic-off", "lg");
            _mic.Add(_micIcon);
            _mic.RegisterCallback<ClickEvent>(_ =>
            {
                if (_source.CanTalk == null || _source.CanTalk()) _source.SetMic?.Invoke(!(_source.MicOn?.Invoke() ?? false));
            });
            voice.Add(_mic);

            var labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("chat-voice-labels");
            _micTitle = new Label("语音") { pickingMode = PickingMode.Ignore };
            _micTitle.AddToClassList("chat-voice-title");
            _voiceStatus = new Label { pickingMode = PickingMode.Ignore };
            _voiceStatus.AddToClassList("chat-voice-status");
            labels.Add(_micTitle);
            labels.Add(_voiceStatus);
            voice.Add(labels);

            voice.Add(UiIcon.Create("volume", "chat-volume-icon"));
            _volume = new AnimatedSlider(0f, 2f, false, 0.05f, v => Mathf.Round(v / 0.05f) * 0.05f);
            _volume.AddToClassList("chat-volume");
            _volume.Changed += v =>
            {
                _source.SetVolume?.Invoke(v);
                ShowVolume(v);
            };
            voice.Add(_volume);
            _volumeValue = new Label { pickingMode = PickingMode.Ignore };
            _volumeValue.AddToClassList("chat-volume-value");
            voice.Add(_volumeValue);
            Add(voice);

            // ---- 消息
            var listHost = new VisualElement();
            listHost.AddToClassList("chat-list-host");
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("etw-scroll");
            _scroll.AddToClassList("chat-list");
            listHost.Add(_scroll);
            _smooth = SmoothScroll.Attach(_scroll);

            _empty = new VisualElement { pickingMode = PickingMode.Ignore };
            _empty.AddToClassList("feed-empty");
            _empty.Add(UiIcon.Create("chat", "lg"));
            var emptyTitle = new Label("还没有消息") { pickingMode = PickingMode.Ignore };
            emptyTitle.AddToClassList("feed-empty-title");
            var emptyHint = new Label("和一起制谱的人打个招呼吧。打开麦克风还可以直接语音。") { pickingMode = PickingMode.Ignore };
            emptyHint.AddToClassList("feed-empty-hint");
            _empty.Add(emptyTitle);
            _empty.Add(emptyHint);
            listHost.Add(_empty);
            Add(listHost);

            // ---- 输入
            var input = new VisualElement();
            input.AddToClassList("chat-input");
            _field = new TextField { maxLength = source.MaxLength };
            _field.AddToClassList("chat-field");
            _field.RegisterCallback<KeyDownEvent>(OnFieldKey, TrickleDown.TrickleDown);
            _field.RegisterCallback<FocusInEvent>(_ => input.AddToClassList("focused"));
            _field.RegisterCallback<FocusOutEvent>(_ => input.RemoveFromClassList("focused"));
            input.Add(_field);

            _send = new VisualElement();
            _send.AddToClassList("st-btn");
            _send.AddToClassList("primary");
            _send.AddToClassList("chat-send");
            _send.Add(UiIcon.Create("send", "sm"));
            _send.Add(new Label("发送") { pickingMode = PickingMode.Ignore });
            _send.RegisterCallback<ClickEvent>(_ => SendCurrent());
            input.Add(_send);
            Add(input);
        }

        /// <summary>页面显示时每 0.25 秒调用：补上新消息、刷新语音状态。</summary>
        public void Refresh()
        {
            IReadOnlyList<ChatLine> lines = _source.Lines();
            _empty.style.display = lines.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (lines.Count > 0 && lines[lines.Count - 1].Id < _newest || lines.Count == 0 && _newest >= 0)
            {
                // 换了房间，记录清空了
                _scroll.Clear();
                _newest = -1;
                _previous = null;
            }

            bool added = false, mineAdded = false;
            float max = _scroll.contentContainer.layout.height - _scroll.contentViewport.layout.height;
            bool atBottom = float.IsNaN(max) || max <= 0f || _scroll.scrollOffset.y >= max - 40f;
            foreach (ChatLine line in lines)
            {
                if (line.Id <= _newest) continue;
                VisualElement row = Build(line, _previous);
                _scroll.Add(row);
                row.AddToClassList("enter");
                row.schedule.Execute(() => row.RemoveFromClassList("enter")).StartingIn(20);
                _previous = line;
                _newest = line.Id;
                added = true;
                mineAdded |= line.Kind == ChatLineKind.Mine;
            }

            if (added && (atBottom || mineAdded))
                _scroll.schedule.Execute(() => _smooth.ScrollTo(float.MaxValue)).StartingIn(30);

            bool canSend = _source.CanSend == null || _source.CanSend();
            _send.EnableInClassList("off", !canSend);
            string placeholder = _source.Placeholder?.Invoke() ?? string.Empty;
            if (_field.textEdition.placeholder != placeholder) _field.textEdition.placeholder = placeholder;

            bool canTalk = _source.CanTalk == null || _source.CanTalk();
            bool micOn = _source.MicOn?.Invoke() ?? false;
            bool speaking = micOn && canTalk && (_source.Speaking?.Invoke() ?? false);
            _mic.EnableInClassList("on", micOn && canTalk);
            _mic.EnableInClassList("off", !canTalk);
            _mic.EnableInClassList("speaking", speaking);
            _micIcon.EnableInClassList("ic-mic", micOn);
            _micIcon.EnableInClassList("ic-mic-off", !micOn);
            _micTitle.text = micOn ? "麦克风已打开" : "麦克风已关闭";
            string status = _source.VoiceStatus?.Invoke() ?? string.Empty;
            if (_voiceStatus.text != status) _voiceStatus.text = status;
            float volume = _source.Volume?.Invoke() ?? 1f;
            if (Mathf.Abs(_volume.Value - volume) > 0.001f) _volume.SetValueWithoutNotify(volume, animate: false);
            ShowVolume(volume);
        }

        private void ShowVolume(float volume)
        {
            _volumeValue.text = Mathf.RoundToInt(volume * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void OnFieldKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                SendCurrent();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                _field.Blur();
                e.StopPropagation();
            }
        }

        private void SendCurrent()
        {
            if (_source.CanSend != null && !_source.CanSend()) return;
            string text = _field.value;
            if (string.IsNullOrWhiteSpace(text)) return;
            _source.Send?.Invoke(text);
            _field.SetValueWithoutNotify(string.Empty);
            _field.schedule.Execute(() => _field.Focus()).StartingIn(1);
        }

        /// <summary>一条消息。和上一条是同一个人、两分钟内说的，就不重复头像和名字。</summary>
        private static VisualElement Build(ChatLine line, ChatLine previous)
        {
            var row = new VisualElement();
            row.AddToClassList("chat-row");
            if (line.Kind == ChatLineKind.System)
            {
                row.AddToClassList("system");
                var note = new Label(line.Text) { pickingMode = PickingMode.Ignore };
                note.AddToClassList("chat-system");
                row.Add(note);
                return row;
            }

            bool mine = line.Kind == ChatLineKind.Mine;
            bool continued = previous != null && previous.Kind == line.Kind && previous.SenderKey == line.SenderKey &&
                             (line.Time - previous.Time).TotalMinutes < 2;
            row.EnableInClassList("mine", mine);
            row.EnableInClassList("cont", continued);

            var avatar = new VisualElement { pickingMode = PickingMode.Ignore };
            avatar.AddToClassList("chat-av");
            avatar.EnableInClassList("host", line.IsHost);
            if (!continued)
            {
                if (line.Avatar != null) avatar.style.backgroundImage = new StyleBackground(line.Avatar);
                else
                {
                    var initial = new Label(string.IsNullOrEmpty(line.Sender) ? "?" : line.Sender.Substring(0, 1).ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                    initial.AddToClassList("feed-av-initial");
                    avatar.Add(initial);
                }
            }

            row.Add(avatar);

            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("chat-col");
            if (!continued)
            {
                var head = new VisualElement { pickingMode = PickingMode.Ignore };
                head.AddToClassList("chat-head");
                var name = new Label(mine ? "你" : line.Sender) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("chat-name");
                var time = new Label(line.Time.ToString("HH:mm")) { pickingMode = PickingMode.Ignore };
                time.AddToClassList("chat-time");
                head.Add(name);
                head.Add(time);
                column.Add(head);
            }

            var bubble = new Label(line.Text) { pickingMode = PickingMode.Ignore };
            bubble.AddToClassList("chat-bubble");
            column.Add(bubble);
            row.Add(column);
            return row;
        }
    }
}
