#nullable disable
using ADOFAI.EditorTweaks.UI;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>设置窗口里的"多人"分类：建房、邀请、成员、加入时选谱面放哪、传输进度。</summary>
    internal static class CoopPage
    {
        public const string CategoryName = "多人制谱";

        /// <summary>侧边面板顶栏状态牌的颜色。</summary>
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

        public static SettingsCategory Build()
        {
            string ignored;

            var status = new InfoRow("wifi", "状态", null,
                () => CoopSession.StatusText,
                tone: () => CoopSession.IsSyncing ? "good" : null,
                dynamicDescription: () => CoopSession.Message);

            var host = new ActionRow("users", "创建房间", null, null,
                    () => CoopSession.CanHost(out string reason) ? "用当前打开的谱面开一个房间（仅好友可见），然后邀请好友加入。" : reason + "。")
                .Button("plus", "创建", CoopSession.Host, primary: true, enabled: () => CoopSession.CanHost(out ignored))
                .VisibleWhen(() => !CoopSession.InSession);

            var invite = new ActionRow("link", "邀请好友",
                    "打开 Steam 邀请界面。好友也可以在 Steam 好友列表里右键你，选「加入游戏」。")
                .Button("link", "邀请", CoopSession.Invite, primary: true)
                .VisibleWhen(() => CoopSession.IsHost);

            var members = new InfoRow("users", "成员", null, () => CoopSession.MembersText)
                .VisibleWhen(() => CoopSession.InSession && CoopSession.MembersText.Length > 0);

            var leave = new ActionRow("logout", "离开房间", null, null,
                    () => CoopSession.IsHost ? "关闭房间，所有人都会断开。谱面文件留在各自电脑上。" : "断开连接，已经下载的谱面和素材会留在本地。")
                .Button("logout", "离开", CoopSession.Leave)
                .VisibleWhen(() => CoopSession.InSession);

            bool Choosing() => CoopSession.State == CoopState.ChoosingFolder;

            var offer = new InfoRow("film", "房主的谱面",
                    "打开房主的谱面会关掉你现在打开的谱面，有没保存的修改请先保存。",
                    () => CoopSession.OfferSongName + "  ·  " + CoopSession.OfferFileName)
                .VisibleWhen(Choosing);

            var suggested = new ActionRow("check", "使用本机找到的这张谱", null, null, () => CoopSession.SuggestedLevelPath ?? string.Empty)
                .Button("check", "使用", CoopSession.ChooseSuggested, primary: true, enabled: () => CoopSession.CanOpenLevel(out ignored))
                .VisibleWhen(() => Choosing() && CoopSession.SuggestedLevelPath != null);

            var existing = new ActionRow("folder", "选择本地已有的谱面",
                    "选你电脑上这张谱的 .adofai：只下载缺少或不一样的文件，覆盖前会把旧文件备份到谱面文件夹的 .coop-backup 里。")
                .Button("folder", "选择…", CoopSession.ChooseExisting, enabled: () => CoopSession.CanOpenLevel(out ignored))
                .VisibleWhen(Choosing);

            var download = new ActionRow("download", "下载到新文件夹",
                    "放到 EditorTweaks 的数据目录里；下次加入同一张谱会直接复用已经下载的文件。")
                .Button("download", "下载", CoopSession.ChooseDownload, enabled: () => CoopSession.CanOpenLevel(out ignored))
                .VisibleWhen(Choosing);

            var transfer = new InfoRow("download", "文件传输", null,
                    () => CoopSession.TransferText, progress: () => CoopSession.TransferProgress)
                .VisibleWhen(() => CoopSession.InSession);

            var help = new InfoRow("info", "怎么用",
                "房主：进入编辑器，打开并保存谱面，点「创建」再邀请好友。好友：接受邀请就会弹出这一页。" +
                "目前每次修改都会同步整份谱面，几个人同时改时以最后到达的为准；收到别人的修改后，本地的撤销记录会清空。",
                () => string.Empty);

            return new SettingsCategory(CategoryName, "users",
                new SettingsSection("房间", status, host, invite, members, leave),
                new SettingsSection("加入谱面", offer, suggested, existing, download),
                new SettingsSection("传输", transfer),
                new SettingsSection("说明", help));
        }
    }
}
