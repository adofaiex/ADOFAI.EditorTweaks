#nullable disable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ADOFAI.EditorTweaks.UI;
using Steamworks;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>Steam 头像缓存：第一次要某人的头像时去 Steam 取（异步），取到之前返回 null（界面先显示名字首字）。</summary>
    internal static class CoopAvatars
    {
        private static readonly Dictionary<ulong, Texture2D> Cache = new Dictionary<ulong, Texture2D>();
        private static readonly HashSet<ulong> Requested = new HashSet<ulong>();

        public static Texture2D Get(ulong steamId)
        {
            if (steamId == 0) return null;
            if (Cache.TryGetValue(steamId, out Texture2D texture)) return texture;
            if (Requested.Add(steamId)) Fetch(steamId);
            return null;
        }

        private static async void Fetch(ulong steamId)
        {
            try
            {
                if (!SteamClient.IsValid) return;
                Steamworks.Data.Image? image = await new Friend(steamId).GetMediumAvatarAsync();
                if (!image.HasValue) return;
                Steamworks.Data.Image value = image.Value;
                int width = (int)value.Width, height = (int)value.Height;
                // Steam 的图是从上往下的，Unity 贴图从下往上，翻一下
                var flipped = new byte[value.Data.Length];
                int stride = width * 4;
                for (int y = 0; y < height; y++) Buffer.BlockCopy(value.Data, y * stride, flipped, (height - 1 - y) * stride, stride);
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                texture.LoadRawTextureData(flipped);
                texture.Apply();
                Cache[steamId] = texture;
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Avatar for " + steamId + " failed: " + exception.Message);
            }
        }
    }

    /// <summary>修改记录：谁在什么时候做了什么（加入、离开、各种修改），侧边面板的"修改记录"页显示。</summary>
    internal static class CoopActivity
    {
        private const int Max = 300;
        private static readonly List<ActivityItem> Items = new List<ActivityItem>();
        private static readonly Regex EventTypePattern = new Regex("\"eventType\":\"(\\w+)\"", RegexOptions.Compiled);
        private static long _nextId = 1;

        public static IReadOnlyList<ActivityItem> All => Items;

        public static void Clear()
        {
            Items.Clear();
        }

        public static void Add(ulong who, string name, string icon, string text, Action jump = null)
        {
            Items.Add(new ActivityItem
            {
                Id = _nextId++,
                Time = DateTime.Now,
                Who = name,
                Avatar = CoopAvatars.Get(who),
                Icon = icon,
                Text = text,
                Jump = jump,
            });
            if (Items.Count > Max) Items.RemoveRange(0, Items.Count - Max);
        }

        /// <summary>把一组修改说成一句话（"新增 旋转，插入 3 块砖"），并给出图标和可以跳转的目标（砖块 ID / 装饰 ID）。</summary>
        public static string Describe(List<CoopOp> ops, CoopModel model, out string icon, out long tile, out long decoration)
        {
            icon = "tune";
            tile = 0;
            decoration = 0;
            int tilesIn = 0, tilesOut = 0, tilesSet = 0, eventsGone = 0, decosGone = 0;
            var eventsNew = new List<string>();
            var eventsChanged = new List<string>();
            var decosNew = new List<string>();
            var decosChanged = new List<string>();
            var settings = new List<string>();
            bool reorder = false;

            foreach (CoopOp op in ops)
            {
                switch (op.Kind)
                {
                    case CoopOpKind.TileInsert:
                        tilesIn += op.Ids.Count;
                        if (tile == 0) tile = op.Ids[0];
                        break;
                    case CoopOpKind.TileDelete:
                        tilesOut += op.Ids.Count;
                        break;
                    case CoopOpKind.TileSet:
                        tilesSet++;
                        if (tile == 0) tile = op.Id;
                        break;
                    case CoopOpKind.Upsert:
                    {
                        string type = EventName(op.Json);
                        if (op.Decoration)
                        {
                            (op.Created ? decosNew : decosChanged).Add(type);
                            if (decoration == 0) decoration = op.Id;
                        }
                        else
                        {
                            (op.Created ? eventsNew : eventsChanged).Add(type);
                            if (tile == 0 && op.FloorTile > 0) tile = op.FloorTile;
                        }

                        break;
                    }

                    case CoopOpKind.Remove:
                        if (op.Decoration) decosGone++;
                        else eventsGone++;
                        break;
                    case CoopOpKind.DecorationOrder:
                        reorder = true;
                        break;
                    case CoopOpKind.Settings:
                        settings.Add(Localized("editor." + op.SettingsType, op.SettingsType));
                        break;
                }
            }

            var parts = new List<string>();
            if (tilesIn > 0) parts.Add("插入 " + tilesIn + " 块砖");
            if (tilesOut > 0) parts.Add("删除 " + tilesOut + " 块砖");
            if (tilesSet > 0) parts.Add("改了 " + tilesSet + " 块砖的角度");
            AddNamed(parts, "新增", eventsNew, "个事件");
            AddNamed(parts, "修改", eventsChanged, "个事件");
            if (eventsGone > 0) parts.Add("删除 " + eventsGone + " 个事件");
            AddNamed(parts, "新增装饰", decosNew, "个");
            AddNamed(parts, "修改装饰", decosChanged, "个");
            if (decosGone > 0) parts.Add("删除 " + decosGone + " 个装饰");
            if (reorder) parts.Add("调整装饰顺序");
            if (settings.Count > 0) parts.Add("修改设置（" + string.Join("、", settings) + "）");

            if (tilesIn + tilesOut + tilesSet > 0) icon = "diamond";
            else if (eventsNew.Count + eventsChanged.Count + eventsGone > 0) icon = "bolt";
            else if (decosNew.Count + decosChanged.Count + decosGone > 0 || reorder) icon = "layers";
            else if (settings.Count > 0) icon = "settings";
            return parts.Count == 0 ? "修改了谱面" : string.Join("，", parts);
        }

        /// <summary>"新增 旋转" / "新增 3 个事件（旋转、设置速度…）"。</summary>
        private static void AddNamed(List<string> parts, string verb, List<string> names, string unit)
        {
            if (names.Count == 0) return;
            if (names.Count == 1)
            {
                parts.Add(verb + " " + names[0]);
                return;
            }

            var distinct = new List<string>();
            foreach (string name in names)
                if (!distinct.Contains(name)) distinct.Add(name);
            string list = string.Join("、", distinct.GetRange(0, Math.Min(3, distinct.Count))) + (distinct.Count > 3 ? "…" : string.Empty);
            parts.Add(verb + " " + names.Count + " " + unit + "（" + list + "）");
        }

        private static string EventName(string json)
        {
            Match match = EventTypePattern.Match(json ?? string.Empty);
            if (!match.Success) return "事件";
            string type = match.Groups[1].Value;
            return Localized("editor." + type, type);
        }

        private static string Localized(string key, string fallback)
        {
            try
            {
                string text = RDString.GetWithCheck(key, out bool exists);
                return exists && !string.IsNullOrEmpty(text) ? text : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
