#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityFileDialog;

namespace ADOFAI.EditorTweaks.Coop
{
    internal enum CoopState
    {
        Idle,
        CreatingRoom,
        Hosting,
        JoiningRoom,
        Connecting,
        ChoosingFolder,
        Downloading,
        Opening,
        Synced,
    }

    /// <summary>
    /// 多人制谱的会话：房间、连接、加入流程、素材文件。谱面同步本身在 CoopSync.cs（同一个类的另一半）。
    /// <para>房主：用当前谱面建一个 Steam 房间（好友可见），开一个 Steam 中继 socket；客人通过邀请或好友列表"加入游戏"进来。</para>
    /// <para>客人进来后先选谱面放哪（本地已有的文件夹 / 下载到新文件夹），只下载缺的或不一样的素材，再打开房主的谱面。</para>
    /// </summary>
    internal static partial class CoopSession
    {
        private static readonly Dictionary<uint, CoopPeer> Peers = new Dictionary<uint, CoopPeer>();
        private static readonly Dictionary<string, IncomingFile> Downloads = new Dictionary<string, IncomingFile>();
        private static readonly Dictionary<string, List<CoopPeer>> FileWaiters = new Dictionary<string, List<CoopPeer>>();

        private static GameObject _runner;
        private static bool _commandLineChecked;

        private static Lobby? _lobby;
        private static CoopHostSocket _socket;
        private static CoopClientConnection _client;
        private static CoopPeer _hostPeer;
        private static float _stateSince;
        private static bool _welcomed;

        // 同步中的谱面文件
        private static string _sessionLevelPath;
        private static float _editorMissingSince = -1f;

        // 客人加入
        private static CoopSnapshot _offer;
        private static string _joinTarget;
        private static int _hashingJobs;
        private static bool _assetsDirty;
        private static string _backupStamp;
        private static long _downloadTotal;
        private static long _downloadDone;

        public static CoopState State { get; private set; }

        public static bool IsHost => State == CoopState.Hosting;

        public static bool InSession => State != CoopState.Idle;

        /// <summary>编辑数据在同步中（房主，或已经打开房主谱面的客人）。</summary>
        public static bool IsSyncing => State == CoopState.Hosting || State == CoopState.Synced;

        /// <summary>和房间里的人连通了（房主，或握手完成的客人）：可以收发语音。</summary>
        public static bool IsConnected => IsHost || _hostPeer != null && _welcomed;

        /// <summary>当前所在的 Steam 房间（聊天用）。</summary>
        public static Lobby? Lobby => InSession ? _lobby : null;

        /// <summary>最近一条提示（出错、断开之类），界面上显示。</summary>
        public static string Message { get; private set; } = string.Empty;

        public static bool MessageIsError { get; private set; }

        public static string HostName { get; private set; } = string.Empty;

        /// <summary>客人：房主的谱面叫什么（选文件夹时显示）。</summary>
        public static string OfferSongName => _offer?.SongName ?? string.Empty;

        public static string OfferFileName => _offer?.LevelFileName ?? string.Empty;

        /// <summary>客人：在本机找到的同一张谱（身份一致），可以直接用；没有是 null。</summary>
        public static string SuggestedLevelPath { get; private set; }

        /// <summary>界面用：一句话的状态。</summary>
        public static string StatusText
        {
            get
            {
                switch (State)
                {
                    case CoopState.CreatingRoom: return "正在创建房间…";
                    case CoopState.Hosting: return "房主 · " + (AcceptedPeers().Count() + 1) + " 人在线";
                    case CoopState.JoiningRoom: return "正在加入房间…";
                    case CoopState.Connecting: return "正在连接房主…";
                    case CoopState.ChoosingFolder: return "请选择谱面放在哪里";
                    case CoopState.Downloading: return "正在下载素材…";
                    case CoopState.Opening: return "正在打开谱面…";
                    case CoopState.Synced: return "已同步 · 房主 " + HostName;
                    default: return "未连接";
                }
            }
        }

        public static string MembersText
        {
            get
            {
                if (IsHost)
                {
                    var names = new List<string> { SteamClient.Name + "（你，房主）" };
                    names.AddRange(AcceptedPeers().Select(p => p.Name));
                    return string.Join("、", names);
                }

                if (_lobby.HasValue && InSession)
                {
                    try
                    {
                        Lobby lobby = _lobby.Value;
                        return string.Join("、", lobby.Members.Select(m =>
                            m.Id == lobby.Owner.Id ? m.Name + "（房主）" : m.IsMe ? m.Name + "（你）" : m.Name));
                    }
                    catch (Exception)
                    {
                        return string.Empty;
                    }
                }

                return string.Empty;
            }
        }

        /// <summary>下载 / 上传进度（0~1）；没有在传就是 -1。</summary>
        public static float TransferProgress
        {
            get
            {
                long total = 0, done = 0;
                foreach (IncomingFile file in Downloads.Values)
                {
                    total += file.Size;
                    done += file.Received;
                }

                if (total > 0) return Mathf.Clamp01((float)(_downloadDone + done) / Math.Max(1L, _downloadTotal));
                return UploadRemaining > 0 ? 0.5f : -1f;
            }
        }

        public static string TransferText
        {
            get
            {
                long downloading = 0;
                foreach (IncomingFile file in Downloads.Values) downloading += file.Size - file.Received;
                long uploading = UploadRemaining;
                if (downloading <= 0 && uploading <= 0) return "没有在传输";
                var parts = new List<string>();
                if (downloading > 0) parts.Add("下载中 " + Downloads.Count + " 个文件，剩 " + FormatSize(downloading));
                if (uploading > 0) parts.Add("上传中，剩 " + FormatSize(uploading));
                return string.Join("；", parts);
            }
        }

        private static long UploadRemaining
        {
            get
            {
                long total = 0;
                foreach (CoopPeer peer in Peers.Values) total += peer.UploadRemaining;
                if (_hostPeer != null) total += _hostPeer.UploadRemaining;
                return total;
            }
        }

        // ================================================================ 启用 / 停用

        public static void Enable()
        {
            if (_runner != null) return;
            _runner = new GameObject("ADOFAI.EditorTweaks Coop");
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<CoopRunner>();
            try
            {
                SteamFriends.OnGameLobbyJoinRequested += OnJoinRequested;
                CoopChat.Enable();
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Steam not available: " + exception.Message);
            }
        }

        public static void Disable()
        {
            Leave(null);
            try
            {
                SteamFriends.OnGameLobbyJoinRequested -= OnJoinRequested;
                CoopChat.Disable();
            }
            catch (Exception)
            {
                // Steam 没初始化
            }

            if (_runner != null) UnityEngine.Object.Destroy(_runner);
            _runner = null;
        }

        /// <summary>游戏没开着时从 Steam 好友列表"加入游戏"，Steam 会带 +connect_lobby &lt;id&gt; 启动游戏。</summary>
        private static void CheckCommandLine()
        {
            if (_commandLineChecked || !SteamClient.IsValid) return;
            _commandLineChecked = true;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id))
                {
                    Join(id);
                    return;
                }
            }
        }

        // ================================================================ 房主：创建房间

        public static bool CanHost(out string reason)
        {
            reason = null;
            if (!SteamClient.IsValid) reason = "Steam 没有运行";
            else if (InSession) reason = "已经在房间里了";
            else if (CoopEditor.Editor == null) reason = "先进入关卡编辑器";
            else if (CoopEditor.LevelPath == null || !File.Exists(CoopEditor.LevelPath)) reason = "先把谱面保存到文件";
            return reason == null;
        }

        public static async void Host()
        {
            if (!CanHost(out string reason))
            {
                SetMessage(reason, error: true);
                return;
            }

            SetState(CoopState.CreatingRoom);
            SetMessage(string.Empty);
            CoopActivity.Clear();
            CoopChat.Clear();
            try
            {
                ConfigureNetworking();
                await WaitForRelayNetwork();
                Lobby? created = await SteamMatchmaking.CreateLobbyAsync(CoopSettings.MaxMembers);
                if (State != CoopState.CreatingRoom)
                {
                    created?.Leave();
                    return;
                }

                if (!created.HasValue)
                {
                    Fail("创建 Steam 房间失败");
                    return;
                }

                Lobby lobby = created.Value;
                _lobby = lobby;
                if (CoopSettings.InviteOnly) lobby.SetPrivate();
                else lobby.SetFriendsOnly();
                lobby.SetJoinable(true);
                lobby.SetData(CoopProtocol.LobbyKey, CoopProtocol.Version.ToString());
                lobby.SetData(CoopProtocol.LobbyLevelKey, CoopEditor.CurrentSongName());

                _socket = SteamNetworkingSockets.CreateRelaySocket<CoopHostSocket>(CoopProtocol.VirtualPort);
                _sessionLevelPath = CoopEditor.LevelPath;
                HostName = SteamClient.Name;
                ResetSync();
                SetState(CoopState.Hosting);
                StartHostSync();
                SetMessage("房间已创建，邀请好友加入吧");
                CoopActivity.Add(SteamClient.SteamId.Value, SteamClient.Name, "users", "用《" + CoopEditor.CurrentSongName() + "》创建了房间");
                Invite();
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Host failed: " + exception);
                Fail("创建房间失败：" + exception.Message);
            }
        }

        public static void Invite()
        {
            if (!_lobby.HasValue) return;
            try
            {
                SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);
            }
            catch (Exception exception)
            {
                SetMessage("打不开 Steam 邀请界面：" + exception.Message, error: true);
            }
        }

        // ================================================================ 客人：加入房间

        private static void OnJoinRequested(Lobby lobby, SteamId friend)
        {
            Join(lobby.Id);
        }

        public static async void Join(SteamId lobbyId)
        {
            if (InSession) Leave(null);
            ResetSync();
            CoopActivity.Clear();
            CoopChat.Clear();
            SetState(CoopState.JoiningRoom);
            SetMessage(string.Empty);
            // 不在编辑器里（主菜单、选关、刚被 Steam 拉起来）：自动进编辑器，进去后面板会自己弹出来
            _wantEditor = CoopEditor.Editor == null;
            SettingsUi.OpenSidePanel(CoopPage.CategoryName);
            try
            {
                ConfigureNetworking();
                await WaitForRelayNetwork();
                Lobby? joined = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
                if (State != CoopState.JoiningRoom)
                {
                    joined?.Leave();
                    return;
                }

                if (!joined.HasValue)
                {
                    Fail("进不去这个房间（可能已经关闭了）");
                    return;
                }

                Lobby lobby = joined.Value;
                _lobby = lobby;
                string version = lobby.GetData(CoopProtocol.LobbyKey);
                if (string.IsNullOrEmpty(version))
                {
                    Fail("这不是 EditorTweaks 的多人制谱房间");
                    return;
                }

                if (version != CoopProtocol.Version.ToString())
                {
                    Fail("房主的 EditorTweaks 版本和你的不一样（多人协议 v" + version + "，你是 v" + CoopProtocol.Version + "），请先更新到同一版本");
                    return;
                }

                Friend owner = lobby.Owner;
                if (owner.IsMe)
                {
                    Fail("这是你自己的房间");
                    return;
                }

                HostName = owner.Name;
                SetState(CoopState.Connecting);
                _connectAttempts = 0;
                _hostSteamId = owner.Id;
                ConnectToHost();
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Join failed: " + exception);
                Fail("加入房间失败：" + exception.Message);
            }
        }

        /// <summary>客人：使用本机找到的同一张谱。</summary>
        public static void ChooseSuggested()
        {
            if (SuggestedLevelPath != null) StartJoinInto(SuggestedLevelPath);
        }

        /// <summary>客人：自己选一个本地已有的 .adofai（同一张谱的旧版本），只补差异。</summary>
        public static void ChooseExisting()
        {
            if (State != CoopState.ChoosingFolder) return;
            string start = SuggestedLevelPath != null ? Path.GetDirectoryName(SuggestedLevelPath) : Persistence.GetLastUsedFolder();
            string picked;
            try
            {
                picked = FileBrowser.PickFile(start, "ADOFAI 谱面", new[] { "adofai" }, "选择你电脑上的这张谱面");
            }
            catch (Exception exception)
            {
                SetMessage("打不开文件选择框：" + exception.Message, error: true);
                return;
            }

            if (string.IsNullOrEmpty(picked)) return;
            picked = Uri.UnescapeDataString(picked.Replace("file:", string.Empty));
            if (!File.Exists(picked))
            {
                SetMessage("找不到这个文件：" + picked, error: true);
                return;
            }

            string identity = CoopEditor.ReadIdentity(picked);
            if (!string.IsNullOrEmpty(_offer.Identity) && identity != _offer.Identity)
                SetMessage("这个谱面的作者 / 曲名和房主的不一样，仍会用房主的版本覆盖（旧文件已备份到 .coop-backup）");
            StartJoinInto(picked);
        }

        /// <summary>客人：下载到 Mod 数据目录下的新文件夹。</summary>
        public static void ChooseDownload()
        {
            if (State != CoopState.ChoosingFolder || _offer == null) return;
            StartJoinInto(Path.Combine(DownloadFolder(_offer), SafeFileName(_offer.LevelFileName)));
        }

        public static bool CanOpenLevel(out string reason)
        {
            reason = CoopEditor.Editor == null ? "先进入关卡编辑器，再选择" : null;
            return reason == null;
        }

        private static void StartJoinInto(string levelPath)
        {
            if (State != CoopState.ChoosingFolder || _offer == null) return;
            if (!CanOpenLevel(out string reason))
            {
                SetMessage(reason, error: true);
                return;
            }

            _joinTarget = levelPath;
            _backupStamp = LevelFiles.NewBackupStamp();
            _downloadTotal = 0;
            _downloadDone = 0;
            SetState(CoopState.Downloading);
            SetMessage("正在比对本地文件…");
            EnsureFiles(_offer.Manifest, Path.GetDirectoryName(levelPath), _hostPeer);
        }

        /// <summary>素材都齐了：把房主的谱面写进目标文件（旧文件先备份），在编辑器里打开。</summary>
        private static void FinishJoin()
        {
            scnEditor editor = CoopEditor.Editor;
            if (editor == null)
            {
                SetMessage("先进入关卡编辑器，谱面才能打开", error: true);
                return;
            }

            try
            {
                string levelDir = Path.GetDirectoryName(_joinTarget);
                Directory.CreateDirectory(levelDir);
                if (File.Exists(_joinTarget) && File.ReadAllText(_joinTarget) != _offer.Json)
                    LevelFiles.Backup(levelDir, Path.GetFileName(_joinTarget), _backupStamp);
                File.WriteAllText(_joinTarget, _offer.Json, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Fail("写入谱面文件失败：" + exception.Message);
                return;
            }

            SetState(CoopState.Opening);
            SetMessage(string.Empty);
            editor.OpenLevel(_joinTarget);
        }

        // ================================================================ 离开

        public static void Leave()
        {
            Leave("已离开房间");
        }

        private static void Leave(string message)
        {
            CoopVoice.Stop();
            foreach (CoopPeer peer in Peers.Values) peer.Dispose();
            Peers.Clear();
            _hostPeer?.Dispose();
            _hostPeer = null;
            try
            {
                _socket?.Close();
            }
            catch (Exception)
            {
                // 已经关了
            }

            _socket = null;
            try
            {
                _client?.Close(false, 0, "leave");
            }
            catch (Exception)
            {
                // 已经关了
            }

            _client = null;
            try
            {
                _lobby?.Leave();
            }
            catch (Exception)
            {
                // Steam 已经关了
            }

            _lobby = null;
            foreach (IncomingFile file in Downloads.Values) file.Abort();
            Downloads.Clear();
            FileWaiters.Clear();
            _offer = null;
            _joinTarget = null;
            SuggestedLevelPath = null;
            _sessionLevelPath = null;
            _hashingJobs = 0;
            _welcomed = false;
            _wantEditor = false;
            _backupStamp = null;
            ResetSync();
            bool wasInSession = InSession;
            if (wasInSession && SteamClient.IsValid) CoopActivity.Add(SteamClient.SteamId.Value, SteamClient.Name, "logout", message ?? "离开了房间");
            SetState(CoopState.Idle);
            if (message != null && wasInSession)
            {
                SetMessage(message);
                CoopEditor.Notify(message);
            }
        }

        private static void Fail(string message)
        {
            Leave(null);
            SetMessage(message, error: true);
            CoopEditor.Notify(message);
        }

        // ================================================================ 每帧

        internal static void Update()
        {
            MainThread.Drain();
            CheckCommandLine();
            MaybeAutoSelfTest();
            if (State == CoopState.Idle) return;
            MaybeEnterEditor();
            CoopVoice.Update();

            try
            {
                _socket?.Receive();
                _client?.Receive();
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Receive failed: " + exception);
            }

            foreach (CoopPeer peer in Peers.Values) peer.Pump();
            _hostPeer?.Pump();

            float now = Time.unscaledTime;
            if ((State == CoopState.Connecting && !_welcomed || State == CoopState.JoiningRoom) && now - _stateSince > 25f)
            {
                Fail("连接房主超时");
                return;
            }

            if (!CheckSessionLevel()) return;

            if (State == CoopState.Downloading && _hashingJobs == 0 && Downloads.Count == 0) FinishJoin();

            if (State == CoopState.Opening) CheckOpened(now);

            if (IsSyncing)
            {
                SyncUpdate(now);
                if (_assetsDirty && Downloads.Count == 0 && _hashingJobs == 0 && CoopEditor.CanApplyNow)
                {
                    _assetsDirty = false;
                    CoopEditor.ReloadAssets();
                }
            }
        }

        /// <summary>同步中打开了别的谱面或离开了编辑器，就退出房间（不然会把别的谱面同步出去）。</summary>
        private static bool CheckSessionLevel()
        {
            if (!IsSyncing) return true;
            scnEditor editor = CoopEditor.Editor;
            if (editor == null)
            {
                if (_editorMissingSince < 0f) _editorMissingSince = Time.unscaledTime;
                if (Time.unscaledTime - _editorMissingSince > 3f)
                {
                    Leave("离开了关卡编辑器，已退出多人房间");
                    return false;
                }

                return true;
            }

            _editorMissingSince = -1f;
            if (editor.isLoading) return true;
            if (!string.Equals(CoopEditor.LevelPath, _sessionLevelPath, StringComparison.OrdinalIgnoreCase))
            {
                Leave("打开了别的谱面，已退出多人房间");
                return false;
            }

            return true;
        }

        private static void CheckOpened(float now)
        {
            scnEditor editor = CoopEditor.Editor;
            if (editor == null || editor.isLoading || now - _stateSince < 0.5f) return;
            if (!string.Equals(CoopEditor.LevelPath, _joinTarget, StringComparison.OrdinalIgnoreCase))
            {
                if (now - _stateSince > 15f) Fail("谱面没能打开");
                return;
            }

            _sessionLevelPath = _joinTarget;
            if (!OnJoinOpened()) return;
            SetState(CoopState.Synced);
            SetMessage("已加入，开始一起制谱吧");
            CoopActivity.Add(SteamClient.SteamId.Value, SteamClient.Name, "users", "加入了 " + HostName + " 的房间");
            CoopEditor.Notify("已加入 " + HostName + " 的多人制谱");
        }

        // ================================================================ 素材文件

        /// <summary>对照清单检查本地文件，缺的或不一样的向 source 要；本机别处有同样内容的直接复制。</summary>
        private static void EnsureFiles(List<ManifestEntry> manifest, string levelDir, CoopPeer source)
        {
            if (source == null || manifest.Count == 0) return;
            var entries = manifest.Where(e => LevelFiles.IsSafeRelative(e.Name)).ToList();
            var paths = entries.Select(e => Path.Combine(levelDir, e.Name)).ToList();
            _hashingJobs++;
            FileHashes.HashAsync(paths, local =>
            {
                _hashingJobs--;
                if (!InSession) return;
                if (_backupStamp == null) _backupStamp = LevelFiles.NewBackupStamp();
                var request = new List<string>();
                for (int i = 0; i < entries.Count; i++)
                {
                    ManifestEntry entry = entries[i];
                    string path = paths[i];
                    if (local.TryGetValue(path, out string sha) && sha == entry.Sha) continue;

                    if (Downloads.TryGetValue(entry.Sha, out IncomingFile incoming))
                    {
                        incoming.Targets.Add(path);
                        continue;
                    }

                    string copy = FileHashes.FindPath(entry.Sha);
                    if (copy != null && CopyLocal(copy, path, levelDir, entry.Sha)) continue;

                    incoming = new IncomingFile(entry.Sha, entry.Size) { LevelDir = levelDir, Source = source };
                    incoming.Targets.Add(path);
                    Downloads[entry.Sha] = incoming;
                    _downloadTotal += entry.Size;
                    request.Add(entry.Sha);
                }

                if (request.Count > 0)
                {
                    BinaryWriter writer = Packet.Writer(out MemoryStream stream);
                    writer.Write(request.Count);
                    foreach (string sha in request) writer.Write(sha);
                    source.Send(CoopLane.Control, CoopMessage.FileRequest, stream.ToArray());
                }

                if (IsHost) ResolveWaiters();
                if (State == CoopState.Downloading)
                    SetMessage(request.Count > 0 ? "需要下载 " + request.Count + " 个文件" : "本地素材都是最新的");
            });
        }

        private static bool CopyLocal(string from, string to, string levelDir, string sha)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return true;
                if (File.Exists(to))
                {
                    LevelFiles.Backup(levelDir, to.Substring(levelDir.Length).TrimStart('/', '\\'), _backupStamp);
                    File.Delete(to);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to);
                FileHashes.Remember(to, sha);
                _assetsDirty = true;
                return true;
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Copy " + from + " failed: " + exception.Message);
                return false;
            }
        }

        private static void OnFileRequest(CoopPeer peer, byte[] payload)
        {
            BinaryReader reader = Packet.Reader(payload);
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string sha = reader.ReadString();
                string path = FileHashes.FindPath(sha);
                if (path != null)
                {
                    peer.QueueFile(sha, path);
                }
                else if (IsHost && (Downloads.ContainsKey(sha) || _hashingJobs > 0))
                {
                    // 房主自己也正在从别人那里收这个文件：收完再转
                    if (!FileWaiters.TryGetValue(sha, out List<CoopPeer> waiters)) FileWaiters[sha] = waiters = new List<CoopPeer>();
                    if (!waiters.Contains(peer)) waiters.Add(peer);
                }
                else
                {
                    peer.SendFileMissing(sha);
                }
            }
        }

        /// <summary>房主：比对完本地文件后，处理"等房主先拿到再转"的请求里已经不用等的那些。</summary>
        private static void ResolveWaiters()
        {
            if (_hashingJobs > 0) return;
            foreach (string sha in FileWaiters.Keys.Where(k => !Downloads.ContainsKey(k)).ToList())
            {
                List<CoopPeer> waiters = FileWaiters[sha];
                FileWaiters.Remove(sha);
                string path = FileHashes.FindPath(sha);
                foreach (CoopPeer waiter in waiters)
                {
                    if (!Peers.ContainsValue(waiter)) continue;
                    if (path != null) waiter.QueueFile(sha, path);
                    else waiter.SendFileMissing(sha);
                }
            }
        }

        private static void OnFileData(CoopPeer peer, byte[] payload)
        {
            BinaryReader reader = Packet.Reader(payload);
            string sha = reader.ReadString();
            reader.ReadInt64();
            long offset = reader.ReadInt64();
            int length = reader.ReadInt32();
            byte[] data = reader.ReadBytes(length);
            if (!Downloads.TryGetValue(sha, out IncomingFile file) || file.Source != peer) return;

            if (!file.Write(offset, data))
            {
                file.Abort();
                Downloads.Remove(sha);
                SetMessage("接收文件出错，已放弃", error: true);
                return;
            }

            if (!file.Complete) return;
            Downloads.Remove(sha);
            _downloadDone += file.Size;
            List<string> placed = file.Finish(_backupStamp ?? LevelFiles.NewBackupStamp(), out string error);
            if (error != null)
            {
                SetMessage("文件 " + string.Join("、", file.Targets.Select(Path.GetFileName)) + " 接收失败：" + error, error: true);
            }
            else
            {
                _assetsDirty = true;
                CoopActivity.Add(peer.SteamId, peer.Name, "download",
                    "传来素材 " + string.Join("、", file.Targets.Select(Path.GetFileName).Distinct()) + "（" + FormatSize(file.Size) + "）");
            }

            if (FileWaiters.TryGetValue(sha, out List<CoopPeer> waiters))
            {
                FileWaiters.Remove(sha);
                string path = placed.FirstOrDefault();
                foreach (CoopPeer waiter in waiters)
                {
                    if (!Peers.ContainsValue(waiter)) continue;
                    if (path != null) waiter.QueueFile(sha, path);
                    else waiter.SendFileMissing(sha);
                }
            }

            if (Downloads.Count == 0)
            {
                _downloadTotal = 0;
                _downloadDone = 0;
            }
        }

        private static void OnFileMissing(CoopPeer peer, byte[] payload)
        {
            string sha = Packet.Reader(payload).ReadString();
            if (!Downloads.TryGetValue(sha, out IncomingFile file) || file.Source != peer) return;
            Downloads.Remove(sha);
            file.Abort();
            SetMessage("对方没有文件 " + string.Join("、", file.Targets.Select(Path.GetFileName)) + "，已跳过", error: true);
            if (FileWaiters.TryGetValue(sha, out List<CoopPeer> waiters))
            {
                FileWaiters.Remove(sha);
                foreach (CoopPeer waiter in waiters) waiter.SendFileMissing(sha);
            }
        }

        // ================================================================ 网络事件（房主）

        internal static void HostPeerConnected(Connection connection, SteamId steamId)
        {
            if (!IsHost) return;
            Peers[connection.Id] = new CoopPeer(connection, steamId.Value, new Friend(steamId).Name);
        }

        internal static void HostPeerDisconnected(Connection connection)
        {
            if (!Peers.TryGetValue(connection.Id, out CoopPeer peer)) return;
            Peers.Remove(connection.Id);
            peer.Dispose();
            CoopVoice.Forget(peer.SteamId);
            foreach (string sha in Downloads.Where(d => d.Value.Source == peer).Select(d => d.Key).ToList())
            {
                Downloads[sha].Abort();
                Downloads.Remove(sha);
            }

            foreach (List<CoopPeer> waiters in FileWaiters.Values) waiters.Remove(peer);
            if (peer.Accepted)
            {
                SetMessage(peer.Name + " 离开了房间");
                CoopActivity.Add(peer.SteamId, peer.Name, "logout", "离开了房间");
                CoopEditor.Notify(peer.Name + " 离开了多人制谱");
            }
        }

        internal static void HostReceived(Connection connection, IntPtr data, int size)
        {
            if (!Peers.TryGetValue(connection.Id, out CoopPeer peer)) return;
            if (!peer.Receive(data, size, out CoopMessage type, out byte[] payload)) return;
            try
            {
                switch (type)
                {
                    case CoopMessage.Hello:
                        OnHello(peer, payload);
                        break;
                    case CoopMessage.Ops when peer.Accepted:
                        HostReceivedOps(peer, CoopBatch.Decode(payload));
                        break;
                    case CoopMessage.ResyncRequest when peer.Accepted:
                        RequestSnapshotFor(peer);
                        break;
                    case CoopMessage.FileRequest when peer.Accepted:
                        OnFileRequest(peer, payload);
                        break;
                    case CoopMessage.FileData when peer.Accepted:
                        OnFileData(peer, payload);
                        break;
                    case CoopMessage.FileMissing when peer.Accepted:
                        OnFileMissing(peer, payload);
                        break;
                    case CoopMessage.Voice when peer.Accepted:
                        // 只接受本人的声音（不能冒充别人），房主自己放，再转给其他人
                        if (CoopVoice.SpeakerOf(payload) != peer.SteamId) break;
                        CoopVoice.Play(payload);
                        foreach (CoopPeer other in AcceptedPeers())
                            if (other != peer) other.SendUnreliable(CoopLane.Voice, CoopMessage.Voice, payload);
                        break;
                }
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Handling " + type + " from " + peer.Name + " failed: " + exception);
            }
        }

        private static void OnHello(CoopPeer peer, byte[] payload)
        {
            BinaryReader reader = Packet.Reader(payload);
            int version = reader.ReadInt32();
            string name = reader.ReadString();
            if (!string.IsNullOrEmpty(name)) peer.Name = name;
            if (version != CoopProtocol.Version)
            {
                BinaryWriter reject = Packet.Writer(out MemoryStream rejectStream);
                reject.Write("房主的多人协议是 v" + CoopProtocol.Version + "，你的是 v" + version + "，请更新到同一版本的 EditorTweaks");
                peer.Send(CoopLane.Control, CoopMessage.Reject, rejectStream.ToArray());
                return;
            }

            peer.Accepted = true;
            BinaryWriter writer = Packet.Writer(out MemoryStream stream);
            writer.Write(SteamClient.Name);
            peer.Send(CoopLane.Control, CoopMessage.Welcome, stream.ToArray());
            RequestSnapshotFor(peer);
            SetMessage(peer.Name + " 加入了房间");
            CoopActivity.Add(peer.SteamId, peer.Name, "users", "加入了房间");
            CoopEditor.Notify(peer.Name + " 加入了多人制谱");
        }

        // ================================================================ 网络事件（客人）

        internal static void ClientConnected()
        {
            if (_hostPeer == null) return;
            BinaryWriter writer = Packet.Writer(out MemoryStream stream);
            writer.Write(CoopProtocol.Version);
            writer.Write(SteamClient.Name);
            _hostPeer.Send(CoopLane.Control, CoopMessage.Hello, stream.ToArray());
        }

        internal static void ClientDisconnected(string reason)
        {
            if (_hostPeer == null) return;
            if (State == CoopState.Connecting && !_welcomed && _connectAttempts < 3)
            {
                // 握手前就断了（比如 Steam 中继证书还没准备好，报 BadCert）：过一会儿自动重连
                Main.Log("[Coop] Connection attempt " + _connectAttempts + " failed (" + reason + "), retrying");
                SetMessage("连接房主失败（" + reason + "），正在重试…");
                RetryConnect();
                return;
            }

            Fail(State == CoopState.Connecting ? "连不上房主（" + reason + "）" : "和房主的连接断开了");
        }

        internal static void ClientReceived(IntPtr data, int size)
        {
            CoopPeer peer = _hostPeer;
            if (peer == null || !peer.Receive(data, size, out CoopMessage type, out byte[] payload)) return;
            try
            {
                switch (type)
                {
                    case CoopMessage.Welcome:
                        _welcomed = true;
                        HostName = Packet.Reader(payload).ReadString();
                        peer.Name = HostName;
                        SetMessage("已连上房主，正在接收谱面…");
                        break;
                    case CoopMessage.Reject:
                        Fail(Packet.Reader(payload).ReadString());
                        break;
                    case CoopMessage.Snapshot:
                        OnClientSnapshot(CoopSnapshot.Decode(payload));
                        break;
                    case CoopMessage.Ops:
                        ClientReceivedOps(CoopBatch.Decode(payload));
                        break;
                    case CoopMessage.StateHash:
                        ClientReceivedHash(payload);
                        break;
                    case CoopMessage.FileRequest:
                        OnFileRequest(peer, payload);
                        break;
                    case CoopMessage.FileData:
                        OnFileData(peer, payload);
                        break;
                    case CoopMessage.FileMissing:
                        OnFileMissing(peer, payload);
                        break;
                    case CoopMessage.Voice:
                        CoopVoice.Play(payload);
                        break;
                }
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Handling " + type + " from host failed: " + exception);
            }
        }

        private static void OnClientSnapshot(CoopSnapshot snapshot)
        {
            switch (State)
            {
                case CoopState.Connecting:
                    _offer = snapshot;
                    ClientOfferChanged(snapshot);
                    SuggestedLevelPath = FindLocalCopy(snapshot);
                    SetState(CoopState.ChoosingFolder);
                    SetMessage(string.Empty);
                    SettingsUi.OpenSidePanel(CoopPage.CategoryName);
                    break;
                case CoopState.ChoosingFolder:
                    _offer = snapshot;
                    ClientOfferChanged(snapshot);
                    break;
                case CoopState.Downloading:
                    _offer = snapshot;
                    ClientOfferChanged(snapshot);
                    EnsureFiles(snapshot.Manifest, Path.GetDirectoryName(_joinTarget), _hostPeer);
                    break;
                case CoopState.Opening:
                case CoopState.Synced:
                    ClientResyncSnapshot(snapshot);
                    break;
            }
        }

        /// <summary>本机有没有同一张谱（谱面身份一致）：当前打开的、最近打开的、以前下载过的。</summary>
        private static string FindLocalCopy(CoopSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(snapshot.Identity)) return null;
            var candidates = new List<string>();
            if (CoopEditor.LevelPath != null) candidates.Add(CoopEditor.LevelPath);
            try
            {
                candidates.Add(Persistence.GetLastOpenedLevel());
            }
            catch (Exception)
            {
                // 没有记录
            }

            candidates.Add(Path.Combine(DownloadFolder(snapshot), SafeFileName(snapshot.LevelFileName)));
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate)) continue;
                if (CoopEditor.ReadIdentity(candidate) == snapshot.Identity) return candidate;
            }

            return null;
        }

        // ================================================================ 小工具

        internal static bool IsLobbyMember(SteamId steamId)
        {
            if (!IsHost || !_lobby.HasValue) return false;
            try
            {
                return _lobby.Value.Members.Any(m => m.Id == steamId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IEnumerable<CoopPeer> AcceptedPeers()
        {
            return Peers.Values.Where(p => p.Accepted);
        }

        /// <summary>
        /// 右上角延迟牌的内容：客人看到自己到房主的延迟；房主看到到各个客人里最慢的那个。
        /// 数据来自 Steam 连接状态（Ping 和连接质量，质量 = 送达的包占比）。没连上 / 没人加入时返回 null。
        /// </summary>
        public static UI.PingInfo Ping()
        {
            try
            {
                var lines = new List<string>();
                int worst = -1;
                float loss = 0f;

                void Measure(CoopPeer peer, string label)
                {
                    ConnectionStatus status = peer.Connection.QuickStatus();
                    int ping = Math.Max(0, status.Ping);
                    float delivered = status.ConnectionQualityLocal;
                    float lost = delivered < 0f ? 0f : Mathf.Clamp01(1f - delivered);
                    worst = Math.Max(worst, ping);
                    loss = Mathf.Max(loss, lost);
                    string line = label + "：" + ping + " ms";
                    if (lost >= 0.01f) line += " · 丢包 " + Mathf.RoundToInt(lost * 100f) + "%";
                    lines.Add(line);
                }

                if (IsHost)
                {
                    foreach (CoopPeer peer in AcceptedPeers()) Measure(peer, peer.Name);
                }
                else if (IsConnected && _hostPeer != null)
                {
                    Measure(_hostPeer, "房主 " + HostName);
                }

                if (worst < 0) return null;
                lines.Insert(0, IsHost ? "到每个人的延迟" : "到房主的延迟");
                return new UI.PingInfo { Ms = worst, Loss = loss, Detail = string.Join("\n", lines) };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>房主改了"仅受邀者可加入"：立刻应用到当前房间。</summary>
        internal static void ApplyRoomVisibility()
        {
            if (!IsHost || !_lobby.HasValue) return;
            try
            {
                Lobby lobby = _lobby.Value;
                if (CoopSettings.InviteOnly) lobby.SetPrivate();
                else lobby.SetFriendsOnly();
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Changing room visibility failed: " + exception.Message);
            }
        }

        /// <summary>发出自己的一段语音：房主发给所有人，客人发给房主（由房主转发）。</summary>
        internal static void SendVoice(byte[] payload)
        {
            if (IsHost)
            {
                foreach (CoopPeer peer in AcceptedPeers()) peer.SendUnreliable(CoopLane.Voice, CoopMessage.Voice, payload);
            }
            else if (_welcomed)
            {
                _hostPeer?.SendUnreliable(CoopLane.Voice, CoopMessage.Voice, payload);
            }
        }

        private static bool _wantEditor;
        private static float _editorRequestedAt = -100f;

        /// <summary>客人还在加入流程中（连接、选文件夹、下载、打开谱面）。</summary>
        public static bool IsJoining => State == CoopState.JoiningRoom || State == CoopState.Connecting ||
                                        State == CoopState.ChoosingFolder || State == CoopState.Downloading || State == CoopState.Opening;

        /// <summary>
        /// 接受邀请时不在编辑器里：等游戏能切场景了就切到编辑器（Steam 刚把游戏拉起来时场景系统还没准备好）。
        /// 切一次没成功（比如被别的过场挡住）就过几秒再试。
        /// </summary>
        private static void MaybeEnterEditor()
        {
            if (!_wantEditor) return;
            if (CoopEditor.Editor != null || !IsJoining)
            {
                _wantEditor = false;
                return;
            }

            if (ADOBase.loader == null || Time.unscaledTime - _editorRequestedAt < 6f) return;
            _editorRequestedAt = Time.unscaledTime;
            try
            {
                scnEditor.levelToOpenOnLoad = null;
                ADOBase.loader.LoadScene("scnEditor");
                Main.Log("[Coop] Entering the level editor to join the room");
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Entering the editor failed: " + exception.Message);
            }
        }

        private static int _connectAttempts;
        private static SteamId _hostSteamId;

        private static void ConnectToHost()
        {
            _connectAttempts++;
            _hostPeer?.Dispose();
            _client = SteamNetworkingSockets.ConnectRelay<CoopClientConnection>(_hostSteamId, CoopProtocol.VirtualPort);
            _hostPeer = new CoopPeer(_client.Connection, _hostSteamId.Value, HostName);
            SetState(CoopState.Connecting);
        }

        private static async void RetryConnect()
        {
            CoopClientConnection old = _client;
            _client = null;
            _hostPeer = null;
            try
            {
                old?.Close(false, 0, "retry");
            }
            catch (Exception)
            {
                // 已经关了
            }

            await System.Threading.Tasks.Task.Delay(2000);
            if (State != CoopState.Connecting) return;
            try
            {
                await WaitForRelayNetwork();
                if (State == CoopState.Connecting) ConnectToHost();
            }
            catch (Exception exception)
            {
                Fail("重新连接房主失败：" + exception.Message);
            }
        }

        /// <summary>等 Steam 中继网络就绪（最多 10 秒）。没就绪就建 socket / 连接，对方会报 BadCert。</summary>
        private static async System.Threading.Tasks.Task WaitForRelayNetwork()
        {
            for (int i = 0; i < 100; i++)
            {
                SteamNetworkingAvailability status = SteamNetworkingUtils.Status;
                if (status == SteamNetworkingAvailability.Current) return;
                if (status == SteamNetworkingAvailability.Failed || status == SteamNetworkingAvailability.CannotTry)
                {
                    Main.Log("[Coop] Relay network unavailable: " + status);
                    return;
                }

                await System.Threading.Tasks.Task.Delay(100);
            }

            Main.Log("[Coop] Relay network still not ready after 10s: " + SteamNetworkingUtils.Status);
        }

        private static void ConfigureNetworking()
        {
            SteamNetworkingUtils.InitRelayNetworkAccess();
            // Steam 默认发送速率上限只有约 256KB/s，传素材会很慢
            SteamNetworkingUtils.SendRateMin = 512 * 1024;
            SteamNetworkingUtils.SendRateMax = 16 * 1024 * 1024;
            SteamNetworkingUtils.SendBufferSize = 8 * 1024 * 1024;
        }

        private static string DownloadFolder(CoopSnapshot snapshot)
        {
            string name = SafeFileName(string.IsNullOrEmpty(snapshot.SongName) ? Path.GetFileNameWithoutExtension(snapshot.LevelFileName) : snapshot.SongName);
            if (name.Length > 60) name = name.Substring(0, 60);
            string hash;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                hash = FileHashes.ToHex(md5.ComputeHash(Encoding.UTF8.GetBytes(snapshot.Identity + "\n" + snapshot.LevelFileName))).Substring(0, 8);
            string root = CoopSettings.DownloadDir;
            if (root == null) return LevelFiles.DataDir("CoopLevels", name + "-" + hash);
            string folder = Path.Combine(root, name + "-" + hash);
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "level.adofai";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / 1024f / 1024f).ToString("0.0") + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        private static void SetState(CoopState state)
        {
            State = state;
            _stateSince = Time.unscaledTime;
        }

        private static void SetMessage(string message, bool error = false)
        {
            Message = message ?? string.Empty;
            MessageIsError = error;
            if (!string.IsNullOrEmpty(message)) Main.Log("[Coop] " + message);
        }

        /// <summary>每帧驱动会话（常驻物体）。</summary>
        private sealed class CoopRunner : MonoBehaviour
        {
            private void Update()
            {
                try
                {
                    CoopSession.Update();
                    if (IsSyncing && (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))) MarkDirty();
                }
                catch (Exception exception)
                {
                    Main.Log("[Coop] Update failed: " + exception);
                }
            }

            private void OnApplicationQuit()
            {
                Leave(null);
            }
        }
    }
}
