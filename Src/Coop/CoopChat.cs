#nullable disable
using System;
using System.Collections.Generic;
using ADOFAI.EditorTweaks.UI;
using Steamworks;
using Steamworks.Data;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 文字聊天：用 Steam 房间自带的聊天（走 Steam 自己的服务器，不依赖我们的 P2P 连接，连接不稳时也能聊）。
    /// 消息只在本次会话内保留；面板没打开或不在聊天页时记未读数，并在编辑器里弹一条提示。
    /// </summary>
    internal static class CoopChat
    {
        public const int MaxLength = 500;
        private const int MaxLines = 300;

        private static readonly List<ChatLine> Lines = new List<ChatLine>();
        private static long _nextId = 1;
        private static bool _subscribed;

        public static IReadOnlyList<ChatLine> All => Lines;

        public static int Unread { get; private set; }

        /// <summary>聊天页正在显示（显示时收到的消息不算未读）。</summary>
        public static bool Viewing { get; set; }

        public static void Enable()
        {
            if (_subscribed) return;
            try
            {
                SteamMatchmaking.OnChatMessage += OnChatMessage;
                SteamMatchmaking.OnLobbyMemberJoined += OnMemberJoined;
                SteamMatchmaking.OnLobbyMemberLeave += OnMemberLeft;
                SteamMatchmaking.OnLobbyMemberDisconnected += OnMemberLeft;
                _subscribed = true;
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Lobby chat unavailable: " + exception.Message);
            }
        }

        public static void Disable()
        {
            if (!_subscribed) return;
            try
            {
                SteamMatchmaking.OnChatMessage -= OnChatMessage;
                SteamMatchmaking.OnLobbyMemberJoined -= OnMemberJoined;
                SteamMatchmaking.OnLobbyMemberLeave -= OnMemberLeft;
                SteamMatchmaking.OnLobbyMemberDisconnected -= OnMemberLeft;
            }
            catch (Exception)
            {
                // Steam 没初始化
            }

            _subscribed = false;
        }

        public static void Clear()
        {
            Lines.Clear();
            Unread = 0;
        }

        public static void MarkRead()
        {
            Unread = 0;
        }

        public static bool CanSend => CoopSession.Lobby.HasValue;

        public static string Placeholder => CanSend ? "说点什么…（回车发送）" : "进入房间后就可以聊天了";

        public static void Send(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) return;
            if (text.Length > MaxLength) text = text.Substring(0, MaxLength);
            Lobby? lobby = CoopSession.Lobby;
            if (!lobby.HasValue)
            {
                System("还没进房间，消息没有发出去");
                return;
            }

            try
            {
                if (!lobby.Value.SendChatString(text)) System("消息发送失败，稍后再试");
            }
            catch (Exception exception)
            {
                System("消息发送失败：" + exception.Message);
            }
        }

        /// <summary>系统提示（有人加入 / 离开之类），显示在聊天里居中的灰字。</summary>
        public static void System(string text)
        {
            Add(new ChatLine { Kind = ChatLineKind.System, Text = text });
        }

        private static void OnChatMessage(Lobby lobby, Friend friend, string text)
        {
            Lobby? current = CoopSession.Lobby;
            if (!current.HasValue || current.Value.Id != lobby.Id || string.IsNullOrEmpty(text)) return;
            bool mine = friend.IsMe;
            Add(new ChatLine
            {
                Kind = mine ? ChatLineKind.Mine : ChatLineKind.Other,
                SenderKey = friend.Id.Value,
                Sender = friend.Name,
                Avatar = CoopAvatars.Get(friend.Id.Value),
                IsHost = lobby.Owner.Id == friend.Id,
                Text = text.Length > MaxLength ? text.Substring(0, MaxLength) : text,
            });

            if (!mine && !Viewing)
            {
                Unread++;
                CoopEditor.Notify(friend.Name + "：" + (text.Length > 40 ? text.Substring(0, 40) + "…" : text));
            }
        }

        private static void OnMemberJoined(Lobby lobby, Friend friend)
        {
            Lobby? current = CoopSession.Lobby;
            if (current.HasValue && current.Value.Id == lobby.Id) System(friend.Name + " 进入了房间");
        }

        private static void OnMemberLeft(Lobby lobby, Friend friend)
        {
            Lobby? current = CoopSession.Lobby;
            if (!current.HasValue || current.Value.Id != lobby.Id) return;
            System(friend.Name + " 离开了房间");
            CoopVoice.Forget(friend.Id.Value);
        }

        private static void Add(ChatLine line)
        {
            line.Id = _nextId++;
            line.Time = DateTime.Now;
            Lines.Add(line);
            if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
        }
    }
}
