#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ADOFAI;
using ADOFAI.EditorTweaks.UI;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 右侧面板里多人制谱的几页：
    /// "房间"（当前谱面卡片 + 建房 / 邀请 / 离开 + 加入时选谱面放哪 + 传输进度）、"修改记录"、"帮助"。
    /// </summary>
    internal static class CoopPage
    {
        /// <summary>客人加入时自动切到这一页。</summary>
        public const string CategoryName = "房间";

        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static string _previewPath;
        private static long _previewStamp;
        private static Texture2D _preview;

        /// <summary>顶栏状态牌的颜色。</summary>
        public static string StatusTone()
        {
            switch (CoopSession.State)
            {
                case CoopState.Idle: return CoopSession.MessageIsError ? "warn" : null;
                case CoopState.Hosting:
                case CoopState.Synced: return "good";
                default: return "busy";
            }
        }

        public static List<SettingsCategory> Build()
        {
            return new List<SettingsCategory> { BuildRoom(), BuildHistory(), BuildHelp() };
        }

        // ---------------------------------------------------------------- 房间

        private static SettingsCategory BuildRoom()
        {
            string ignored;

            var status = new InfoRow("wifi", "状态", null,
                () => CoopSession.StatusText,
                tone: () => CoopSession.IsSyncing ? "good" : CoopSession.MessageIsError ? "warn" : null,
                dynamicDescription: () => CoopSession.Message);

            var host = new ActionRow("users", "创建房间", null, null,
                    () => CoopSession.CanHost(out string reason) ? "用当前谱面开一个房间（仅好友可见），然后邀请好友加入。" : reason + "。")
                .Button("plus", "创建", CoopSession.Host, primary: true, enabled: () => CoopSession.CanHost(out ignored))
                .VisibleWhen(() => !CoopSession.InSession);

            var invite = new ActionRow("link", "邀请好友",
                    "打开 Steam 邀请界面。好友也可以在 Steam 好友列表里右键你，选「加入游戏」。")
                .Button("link", "邀请", CoopSession.Invite, primary: true)
                .VisibleWhen(() => CoopSession.IsHost);

            var leave = new ActionRow("logout", "离开房间", null, null,
                    () => CoopSession.IsHost ? "关闭房间，所有人都会断开。谱面文件留在各自电脑上。" : "断开连接，已经下载的谱面和素材会留在本地。")
                .Button("logout", "离开", CoopSession.Leave)
                .VisibleWhen(() => CoopSession.InSession);

            bool Choosing() => CoopSession.State == CoopState.ChoosingFolder;

            var suggested = new ActionRow("check", "使用本机找到的这张谱", null, null, () => CoopSession.SuggestedLevelPath ?? string.Empty)
                .Button("check", "使用", CoopSession.ChooseSuggested, primary: true, enabled: () => CoopSession.CanOpenLevel(out ignored))
                .VisibleWhen(() => Choosing() && CoopSession.SuggestedLevelPath != null);

            var existing = new ActionRow("folder", "选择本地已有的谱面",
                    "选你电脑上这张谱的 .adofai：只下载缺少或不一样的文件，被覆盖的旧文件备份在谱面文件夹的 .coop-backup 里。")
                .Button("folder", "选择…", CoopSession.ChooseExisting, enabled: () => CoopSession.CanOpenLevel(out ignored))
                .VisibleWhen(Choosing);

            var download = new ActionRow("download", "下载到新文件夹",
                    "放到 EditorTweaks 的数据目录里；下次加入同一张谱会直接复用已经下载的文件。")
                .Button("download", "下载", CoopSession.ChooseDownload, enabled: () => CoopSession.CanOpenLevel(out ignored))
                .VisibleWhen(Choosing);

            var transfer = new InfoRow("download", "文件传输", null,
                    () => CoopSession.TransferText, progress: () => CoopSession.TransferProgress)
                .VisibleWhen(() => CoopSession.InSession && CoopSession.TransferProgress >= 0f);

            var card = new LevelCard();
            return new SettingsCategory(CategoryName, "users",
                new SettingsSection("房间", status, host, invite, leave),
                new SettingsSection("打开房主的谱面（会关掉你现在打开的谱面）", suggested, existing, download),
                new SettingsSection("传输", transfer))
            {
                Header = card,
                Refresh = () => card.SetInfo(LevelInfo()),
            };
        }

        /// <summary>卡片显示的谱面：客人选文件夹时是房主的谱面，其他时候是编辑器里打开的谱面。</summary>
        private static LevelCardInfo LevelInfo()
        {
            var info = new LevelCardInfo();
            if (CoopSession.State == CoopState.ChoosingFolder)
            {
                info.Title = Clean(CoopSession.OfferSongName);
                info.Subtitle = "房主 " + CoopSession.HostName + " 的谱面";
                info.Path = CoopSession.OfferFileName;
                return info;
            }

            scnEditor editor = CoopEditor.Editor;
            if (editor == null)
            {
                info.Title = "没有打开谱面";
                info.Subtitle = "进入关卡编辑器后，这里会显示当前的谱面";
                return info;
            }

            LevelData data = editor.levelData;
            string path = CoopEditor.LevelPath;
            string song = Clean(data.song);
            info.Title = song.Length > 0 ? song : path != null ? Path.GetFileNameWithoutExtension(path) : "新谱面";
            string artist = Clean(data.artist), author = Clean(data.author);
            var subtitle = new List<string>();
            if (artist.Length > 0) subtitle.Add(artist);
            if (author.Length > 0) subtitle.Add("谱师 " + author);
            info.Subtitle = string.Join("  ·  ", subtitle);
            info.Path = path ?? "还没保存到文件";

            int tiles = data.isOldLevel ? (data.pathData ?? string.Empty).Length : data.angleData.Count;
            info.Stats.Add(new KeyValuePair<string, string>("砖块", tiles.ToString("N0", CultureInfo.InvariantCulture)));
            info.Stats.Add(new KeyValuePair<string, string>("事件", data.levelEvents.Count.ToString("N0", CultureInfo.InvariantCulture)));
            info.Stats.Add(new KeyValuePair<string, string>("装饰", data.decorations.Count.ToString("N0", CultureInfo.InvariantCulture)));
            try
            {
                info.Stats.Add(new KeyValuePair<string, string>("BPM", data.bpm.ToString("0.##", CultureInfo.InvariantCulture)));
            }
            catch (Exception)
            {
                // 没有 BPM
            }

            info.Preview = path == null ? null : Preview(Path.GetDirectoryName(path), data);
            return info;
        }

        /// <summary>封面：谱面的预览图，没有就用背景图。按路径和修改时间缓存。</summary>
        private static Texture2D Preview(string levelDir, LevelData data)
        {
            string name = null;
            try
            {
                name = !string.IsNullOrEmpty(data.previewImage) ? data.previewImage : data.bgImage;
            }
            catch (Exception)
            {
                // 设置里没有这一项
            }

            if (string.IsNullOrEmpty(name) || !LevelFiles.IsSafeRelative(name)) return null;
            string path = Path.Combine(levelDir, name);
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length > 16 * 1024 * 1024) return null;
                long stamp = file.LastWriteTimeUtc.Ticks ^ file.Length;
                if (path == _previewPath && stamp == _previewStamp) return _preview;
                _previewPath = path;
                _previewStamp = stamp;
                if (_preview != null) UnityEngine.Object.Destroy(_preview);
                // 用游戏自己的读图方法，顺便缩到 256 以内
                _preview = TextureManager.LoadTexture(path, out LoadResult _, 256);
                return _preview;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Clean(string text)
        {
            return Tags.Replace(text ?? string.Empty, string.Empty).Trim();
        }

        // ---------------------------------------------------------------- 修改记录

        private static SettingsCategory BuildHistory()
        {
            var feed = new ActivityFeed(() => CoopActivity.All);
            return new SettingsCategory("修改记录", "history")
            {
                Content = feed,
                Refresh = feed.Refresh,
            };
        }

        // ---------------------------------------------------------------- 帮助

        private static SettingsCategory BuildHelp()
        {
            SettingRow Tip(string icon, string title, string text) => new InfoRow(icon, title, text, () => string.Empty);

            var selfTest = new ActionRow("sparkle", "同步自检",
                    "在当前谱面的拷贝上模拟三个人同时随机修改 200 轮，检查大家最后的谱面是否完全一致。不会改动你的谱面。")
                .Button("play", "运行", CoopSession.RunSelfTest, enabled: () => CoopEditor.Editor != null && !CoopSession.InSession);
            var selfTestResult = new InfoRow("info", "自检结果", null, () => string.Empty, dynamicDescription: () => CoopSession.Message)
                .VisibleWhen(() => !CoopSession.InSession && CoopSession.Message.StartsWith("同步自检", StringComparison.Ordinal));

            return new SettingsCategory("帮助", "info",
                new SettingsSection("开始",
                    Tip("users", "房主开房间", "进入关卡编辑器，打开并保存谱面，在「房间」页点「创建」，再邀请 Steam 好友。"),
                    Tip("link", "好友加入", "接受邀请，或在 Steam 好友列表里右键房主选「加入游戏」。面板会自动弹出，选好谱面放在哪里就开始下载。"),
                    Tip("folder", "已经有这张谱", "选「本地已有的谱面」只会下载缺少或不一样的文件；被覆盖的旧文件备份在谱面文件夹的 .coop-backup 里。")),
                new SettingsSection("同步规则",
                    Tip("sync", "只同步改动", "每次修改只发送改动的部分。几个人改不同的东西互不影响；同时改到同一处时，以房主收到的先后为准。"),
                    Tip("check", "自动校对", "大家停手后房主会校对一次整份谱面，发现不一致会自动重新同步。"),
                    Tip("reset", "撤销", "收到别人的修改后，本地的撤销记录会清空（以后会改成各自撤销自己的修改）。"),
                    Tip("image", "素材文件", "谱面用到的音乐、图片、视频会自动传给大家，内容相同的文件不会重复传。")),
                new SettingsSection("工具", selfTest, selfTestResult));
        }
    }
}
