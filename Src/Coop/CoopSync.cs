#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADOFAI;
using Steamworks;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 谱面同步（第二阶段：增量）。
    /// <para>本地改了谱面：和 <see cref="CoopModel"/> 记录比，只把改动（砖块插删改、事件 / 装饰的增删改、装饰顺序、设置）发出去。</para>
    /// <para>房主给所有修改排一个全局序号，转发给所有人（包括作者本人）。大家按序号应用，所以结果一致；
    /// 作者自己的修改在本地已经生效，收到回执时只有"中间夹了别人的修改"才重放一遍（同一个东西两人都改了，以房主的顺序为准）。</para>
    /// <para>房主在大家都停手一会儿后广播一次整份谱面的校验值；谁对不上就请房主重发整份谱面，所以不会一直不同步。</para>
    /// </summary>
    internal static partial class CoopSession
    {
        private static readonly List<Outgoing> OutQueue = new List<Outgoing>();
        private static readonly Queue<KeyValuePair<CoopPeer, CoopBatch>> HostInbox = new Queue<KeyValuePair<CoopPeer, CoopBatch>>();
        private static readonly List<CoopBatch> ClientInbox = new List<CoopBatch>();
        private static readonly HashSet<CoopPeer> SnapshotRequests = new HashSet<CoopPeer>();
        private static readonly Dictionary<int, int> ForeignAtSend = new Dictionary<int, int>();

        private static CoopModel _model = new CoopModel();
        private static bool _dirty;
        private static float _dirtyAt;
        private static int _authorSeq;
        private static int _ownAcked;

        // 房主
        private static int _seq;
        private static int _lastHashSeq;
        private static float _lastChangeAt;
        private static bool _snapshotBuilding;

        // 客人
        private static int _appliedSeq;
        private static int _foreignApplied;
        private static int _pendingHashSeq = -1;
        private static string _pendingHash;
        private static bool _awaitingResync;

        /// <summary>收到整份谱面后，本地读出来的和房主的仍然不一样（读谱环境不同）：再校验也没用，关掉自动校对免得一直重新同步。</summary>
        private static bool _hashCheckDisabled;
        private static CoopSnapshot _resync;

        private sealed class Outgoing
        {
            public CoopBatch Batch;
            public bool Ready;
        }

        private static uint MyTag => SteamClient.IsValid ? SteamClient.SteamId.AccountId : 1u;

        private static string LevelDir => Path.GetDirectoryName(_sessionLevelPath);

        private static void ResetSync()
        {
            OutQueue.Clear();
            HostInbox.Clear();
            ClientInbox.Clear();
            SnapshotRequests.Clear();
            ForeignAtSend.Clear();
            _model = new CoopModel();
            _dirty = false;
            _authorSeq = 0;
            _ownAcked = 0;
            _seq = 0;
            _lastHashSeq = 0;
            _snapshotBuilding = false;
            _appliedSeq = 0;
            _foreignApplied = 0;
            _pendingHashSeq = -1;
            _pendingHash = null;
            _awaitingResync = false;
            _hashCheckDisabled = false;
            _resync = null;
            _assetsDirty = false;
            _editorMissingSince = -1f;
            CoopIds.Reset(MyTag);
        }

        /// <summary>房主开房：给当前谱面编上初始 ID，作为同步的起点。</summary>
        private static void StartHostSync()
        {
            scnEditor editor = CoopEditor.Editor;
            if (editor != null) _model.BuildFresh(editor.levelData);
            _lastChangeAt = Time.unscaledTime;
        }

        /// <summary>编辑器里发生了一次修改（由 Harmony 补丁调用）。真正比对在停手 0.25 秒后。</summary>
        public static void MarkDirty()
        {
            if (!IsSyncing) return;
            _dirty = true;
            _dirtyAt = Time.unscaledTime;
        }

        private static void SyncUpdate(float now)
        {
            if (_dirty && now - _dirtyAt > 0.25f && CoopEditor.CanApplyNow) FlushLocalEdit();
            PumpOutgoing();
            if (IsHost)
            {
                ProcessHostInbox();
                ServeSnapshots();
                MaybeBroadcastHash(now);
            }
            else
            {
                ProcessClientInbox();
                MaybeCheckHash();
            }
        }

        private static bool _autoTestDone;
        private static bool _autoTestEditorRequested;

        /// <summary>
        /// 开发用：Mod 文件夹里有 coop-selftest.flag 时，第一次在编辑器里打开谱面后自动跑一次自检，
        /// 结果写到 Mod 文件夹的 coop-selftest.log（不用手动点按钮）。
        /// </summary>
        private static void MaybeAutoSelfTest()
        {
            if (_autoTestDone || Main.Mod == null) return;
            string flag = Path.Combine(Main.Mod.Path, "coop-selftest.flag");
            if (!File.Exists(flag))
            {
                _autoTestDone = true;
                return;
            }

            // 标记文件里写了谱面路径：还没进编辑器就自己带着这张谱进去
            scnEditor editor = CoopEditor.Editor;
            // 第一行是谱面路径；后面几行是选项：panel = 打开右侧面板（截图检查界面用），noselftest = 不跑自检
            string[] lines = File.ReadAllLines(flag);
            string wanted = lines.Length > 0 ? lines[0].Trim() : string.Empty;
            bool openPanel = lines.Any(l => l.Trim() == "panel");
            bool skipTest = lines.Any(l => l.Trim() == "noselftest");
            if (editor == null)
            {
                if (_autoTestEditorRequested || wanted.Length == 0 || Time.realtimeSinceStartup < 10f || ADOBase.loader == null) return;
                _autoTestEditorRequested = true;
                scnEditor.levelToOpenOnLoad = wanted;
                ADOBase.loader.LoadScene("scnEditor");
                return;
            }

            if (editor.isLoading || !editor.initialized || CoopEditor.LevelPath == null) return;
            _autoTestDone = true;
            if (openPanel) SettingsUi.OpenSidePanel(CoopPage.CategoryName);
            if (skipTest) return;
            string report;
            try
            {
                report = CoopSelfTest.Run(editor.levelData, 200);
            }
            catch (Exception exception)
            {
                report = "自检出错：" + exception;
            }

            File.WriteAllText(Path.Combine(Main.Mod.Path, "coop-selftest.log"), CoopEditor.LevelPath + Environment.NewLine + report);
        }

        /// <summary>在当前谱面的拷贝上跑一遍增量同步的单机自检，结果显示在状态行。</summary>
        public static void RunSelfTest()
        {
            scnEditor editor = CoopEditor.Editor;
            if (editor == null)
            {
                SetMessage("先进入关卡编辑器", error: true);
                return;
            }

            if (InSession)
            {
                SetMessage("自检会重置 ID 计数，请先离开房间再运行", error: true);
                return;
            }

            try
            {
                string report = CoopSelfTest.Run(editor.levelData, 200);
                SetMessage(report, error: !report.Contains("全部一致"));
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Self test crashed: " + exception);
                SetMessage("自检出错：" + exception.Message, error: true);
            }
        }

        // ================================================================ 面板用：参与者、修改记录、跳转

        /// <summary>顶栏头像：房主在最前，自己标"你"。没进房间时只有自己。</summary>
        public static IReadOnlyList<UI.PersonInfo> People()
        {
            var people = new List<UI.PersonInfo>();
            if (!SteamClient.IsValid) return people;
            ulong me = SteamClient.SteamId.Value;
            void Add(ulong id, string name, bool host)
            {
                people.Add(new UI.PersonInfo
                {
                    Key = id,
                    Name = name,
                    Avatar = CoopAvatars.Get(id),
                    IsHost = host,
                    Badge = id == me ? (host ? "你 · 房主" : "你") : host ? "房主" : null,
                });
            }

            if (IsHost)
            {
                Add(me, SteamClient.Name, true);
                foreach (CoopPeer peer in AcceptedPeers()) Add(peer.SteamId, peer.Name, false);
            }
            else if (_lobby.HasValue && InSession)
            {
                try
                {
                    Steamworks.Data.Lobby lobby = _lobby.Value;
                    ulong owner = lobby.Owner.Id.Value;
                    Add(owner, lobby.Owner.Name, true);
                    foreach (Friend member in lobby.Members)
                        if (member.Id.Value != owner) Add(member.Id.Value, member.Name, false);
                }
                catch (Exception)
                {
                    // 房间信息还没到
                }
            }
            else
            {
                Add(me, SteamClient.Name, false);
            }

            return people;
        }

        private static string NameOf(ulong steamId)
        {
            foreach (UI.PersonInfo person in People())
                if (person.Key == steamId) return person.Name;
            try
            {
                return new Friend(steamId).Name;
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static void LogEdit(ulong who, string name, List<CoopOp> ops)
        {
            string text = CoopActivity.Describe(ops, _model, out string icon, out long tile, out long decoration);
            Action jump = tile == 0 && decoration == 0 ? null : (Action)(() => JumpTo(tile, decoration));
            CoopActivity.Add(who, name, icon, text, jump);
        }

        /// <summary>修改记录里点一条：选中那个装饰，或者跳到那块砖。</summary>
        private static void JumpTo(long tile, long decoration)
        {
            scnEditor editor = CoopEditor.Editor;
            if (editor == null || !IsSyncing || !CoopEditor.CanApplyNow) return;
            try
            {
                if (decoration != 0 && _model.Items.TryGetValue(decoration, out CoopModel.Item item) && !item.Parked)
                {
                    editor.DeselectAllDecorations();
                    editor.SelectDecoration(item.Obj);
                    return;
                }

                int floor = _model.FloorOf(tile);
                if (floor >= 0 && floor < editor.floors.Count) editor.SelectFloor(editor.floors[floor]);
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Jump failed: " + exception.Message);
            }
        }

        // ================================================================ 本地修改 → 发出去

        private static void FlushLocalEdit()
        {
            _dirty = false;
            scnEditor editor = CoopEditor.Editor;
            if (editor == null) return;
            List<CoopOp> ops;
            try
            {
                ops = _model.Diff(editor.levelData);
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Diff failed: " + exception);
                ops = null;
            }

            if (ops == null)
            {
                // 没法用修改描述（谱面格式变了之类）：房主重发整份给所有人，客人请房主重发
                if (IsHost)
                {
                    _model.BuildFresh(editor.levelData);
                    foreach (CoopPeer peer in AcceptedPeers()) SnapshotRequests.Add(peer);
                }
                else
                {
                    RequestResync("本地谱面格式变了");
                }

                return;
            }

            if (ops.Count == 0) return;
            LogEdit(SteamClient.SteamId.Value, SteamClient.Name, ops);
            var batch = new CoopBatch { Author = SteamClient.SteamId.Value, AuthorSeq = ++_authorSeq, Ops = ops };
            var outgoing = new Outgoing { Batch = batch };
            OutQueue.Add(outgoing);
            if (!IsHost) ForeignAtSend[batch.AuthorSeq] = _foreignApplied;

            // 这次改动引用到的素材，算好哈希附在后面（收到的人缺文件就来要）
            var touched = new List<LevelEvent>();
            foreach (CoopOp op in ops)
            {
                if (op.Kind == CoopOpKind.Upsert && _model.Items.TryGetValue(op.Id, out CoopModel.Item item)) touched.Add(item.Obj);
                if (op.Kind == CoopOpKind.Settings)
                    touched.AddRange(editor.levelData.settings.Where(x => x.eventType.ToString() == op.SettingsType));
            }

            string levelDir = LevelDir;
            List<string> names = touched.Count > 0 ? LevelFiles.CollectReferenced(touched, levelDir) : new List<string>();
            if (names.Count == 0)
            {
                outgoing.Ready = true;
                return;
            }

            var paths = names.Select(n => Path.Combine(levelDir, n)).ToList();
            FileHashes.HashAsync(paths, hashes =>
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (!hashes.TryGetValue(paths[i], out string sha) || sha == null) continue;
                    try
                    {
                        batch.Manifest.Add(new ManifestEntry(names[i].Replace('\\', '/'), sha, new FileInfo(paths[i]).Length));
                    }
                    catch (Exception)
                    {
                        // 文件刚被删了
                    }
                }

                outgoing.Ready = true;
            });
        }

        /// <summary>按顺序发出已经准备好的修改（素材哈希还没算完的那份会挡住后面的，保证顺序）。</summary>
        private static void PumpOutgoing()
        {
            while (OutQueue.Count > 0 && OutQueue[0].Ready)
            {
                CoopBatch batch = OutQueue[0].Batch;
                OutQueue.RemoveAt(0);
                if (IsHost)
                {
                    batch.Seq = ++_seq;
                    Broadcast(CoopMessage.Ops, batch.Encode());
                    _lastChangeAt = Time.unscaledTime;
                }
                else
                {
                    _hostPeer?.Send(CoopLane.Control, CoopMessage.Ops, batch.Encode());
                }
            }
        }

        private static void Broadcast(CoopMessage type, byte[] payload)
        {
            foreach (CoopPeer peer in AcceptedPeers()) peer.Send(CoopLane.Control, type, payload);
        }

        // ================================================================ 房主：排序、应用、转发

        private static void HostReceivedOps(CoopPeer peer, CoopBatch batch)
        {
            batch.Author = peer.SteamId;
            HostInbox.Enqueue(new KeyValuePair<CoopPeer, CoopBatch>(peer, batch));
        }

        /// <summary>
        /// 房主按收到的顺序处理客人的修改。自己有还没比对 / 没发出去的修改时先等，
        /// 让自己的先排进去（否则房主本地的先后和发给大家的先后不一致）。
        /// </summary>
        private static void ProcessHostInbox()
        {
            if (HostInbox.Count == 0 || _dirty || OutQueue.Count > 0 || !CoopEditor.CanApplyNow) return;
            while (HostInbox.Count > 0)
            {
                KeyValuePair<CoopPeer, CoopBatch> item = HostInbox.Dequeue();
                if (!Peers.ContainsValue(item.Key)) continue;
                CoopBatch batch = item.Value;
                LogEdit(item.Key.SteamId, item.Key.Name, batch.Ops);
                if (!CoopEditor.ApplyOps(_model, batch.Ops, out string error))
                    SetMessage("应用 " + item.Key.Name + " 的修改失败：" + error, error: true);
                batch.Seq = ++_seq;
                Broadcast(CoopMessage.Ops, batch.Encode());
                EnsureFiles(batch.Manifest, LevelDir, item.Key);
                _lastChangeAt = Time.unscaledTime;
            }
        }

        /// <summary>有人要整份谱面（刚加入、校验不一致）：排队，等房主这边没有未发出的修改时打包。</summary>
        private static void RequestSnapshotFor(CoopPeer peer)
        {
            if (peer != null && peer.Accepted) SnapshotRequests.Add(peer);
        }

        private static void ServeSnapshots()
        {
            if (SnapshotRequests.Count == 0 || _snapshotBuilding || _dirty || OutQueue.Count > 0 || HostInbox.Count > 0) return;
            scnEditor editor = CoopEditor.Editor;
            if (editor == null || !CoopEditor.CanApplyNow) return;

            var peers = SnapshotRequests.ToList();
            SnapshotRequests.Clear();
            BinaryWriter tableWriter = Packet.Writer(out MemoryStream tableStream);
            _model.WriteIdTable(tableWriter);
            var snapshot = new CoopSnapshot
            {
                Seq = _seq,
                IdTable = tableStream.ToArray(),
                LevelFileName = Path.GetFileName(_sessionLevelPath),
                Identity = CoopEditor.CurrentIdentity(),
                SongName = CoopEditor.CurrentSongName(),
                Json = CoopEditor.EncodeLevel(),
            };

            string levelDir = LevelDir;
            List<string> names = LevelFiles.CollectReferenced(editor.levelData, levelDir);
            var paths = names.Select(n => Path.Combine(levelDir, n)).ToList();
            _snapshotBuilding = true;
            FileHashes.HashAsync(paths, hashes =>
            {
                _snapshotBuilding = false;
                if (!IsHost) return;
                for (int i = 0; i < names.Count; i++)
                {
                    if (!hashes.TryGetValue(paths[i], out string sha) || sha == null) continue;
                    try
                    {
                        snapshot.Manifest.Add(new ManifestEntry(names[i].Replace('\\', '/'), sha, new FileInfo(paths[i]).Length));
                    }
                    catch (Exception)
                    {
                        // 文件刚被删了
                    }
                }

                byte[] payload = snapshot.Encode();
                foreach (CoopPeer peer in peers)
                    if (peer.Accepted && Peers.ContainsValue(peer)) peer.Send(CoopLane.Control, CoopMessage.Snapshot, payload);
            });
        }

        /// <summary>大家停手 1.5 秒后，房主广播一次"到第几号修改为止，谱面的校验值是多少"。</summary>
        private static void MaybeBroadcastHash(float now)
        {
            if (_seq == _lastHashSeq || now - _lastChangeAt < 1.5f) return;
            if (_dirty || OutQueue.Count > 0 || HostInbox.Count > 0 || !CoopEditor.CanApplyNow) return;
            if (!AcceptedPeers().Any())
            {
                _lastHashSeq = _seq;
                return;
            }

            _lastHashSeq = _seq;
            BinaryWriter writer = Packet.Writer(out MemoryStream stream);
            writer.Write(_seq);
            writer.Write(CoopEditor.HashLevel());
            Broadcast(CoopMessage.StateHash, stream.ToArray());
        }

        // ================================================================ 客人：应用房主排好序的修改

        private static void ClientReceivedOps(CoopBatch batch)
        {
            ClientInbox.Add(batch);
        }

        /// <summary>加入流程中房主的整份谱面更新了：它已经包含的修改不用再排队。</summary>
        private static void ClientOfferChanged(CoopSnapshot snapshot)
        {
            ClientInbox.RemoveAll(b => b.Seq <= snapshot.Seq);
        }

        /// <summary>客人打开了房主的谱面：按 ID 表编号，从快照对应的序号开始接收修改。</summary>
        private static bool OnJoinOpened()
        {
            scnEditor editor = CoopEditor.Editor;
            if (editor == null) return false;
            if (!_model.AssignFromTable(editor.levelData, _offer.IdTable, out string problem))
            {
                Main.Log("[Coop] Join id table mismatch: " + problem);
                RequestResync(problem);
            }

            _appliedSeq = _offer.Seq;
            ClientInbox.RemoveAll(b => b.Seq <= _appliedSeq);
            CheckReadsLikeHost(_offer.Json, "加入时");
            return true;
        }

        private static void ProcessClientInbox()
        {
            if (State != CoopState.Synced || !CoopEditor.CanApplyNow) return;
            if (_resync == null && ClientInbox.Count == 0) return;

            // 本地有还没比对的修改：先比对发出去（它会排在这些修改后面），再应用别人的
            if (_dirty) FlushLocalEdit();

            if (_resync != null) ApplyResync();

            while (ClientInbox.Count > 0)
            {
                CoopBatch batch = ClientInbox[0];
                ClientInbox.RemoveAt(0);
                if (batch.Seq <= _appliedSeq) continue;
                _appliedSeq = batch.Seq;

                if (batch.Author == SteamClient.SteamId.Value)
                {
                    // 自己的修改回来了：本地早就生效了；只有中间夹了别人的修改，才按房主的顺序重放一遍
                    _ownAcked = Math.Max(_ownAcked, batch.AuthorSeq);
                    bool foreignBetween = !ForeignAtSend.TryGetValue(batch.AuthorSeq, out int foreignAtSend) || _foreignApplied > foreignAtSend;
                    ForeignAtSend.Remove(batch.AuthorSeq);
                    if (!foreignBetween) continue;
                }
                else
                {
                    _foreignApplied++;
                    LogEdit(batch.Author, NameOf(batch.Author), batch.Ops);
                    EnsureFiles(batch.Manifest, LevelDir, _hostPeer);
                }

                if (!CoopEditor.ApplyOps(_model, batch.Ops, out string error))
                    SetMessage("应用别人的修改失败：" + error, error: true);
            }
        }

        private static void ClientReceivedHash(byte[] payload)
        {
            BinaryReader reader = Packet.Reader(payload);
            _pendingHashSeq = reader.ReadInt32();
            _pendingHash = reader.ReadString();
        }

        /// <summary>客人：和房主同一个序号、自己也没有在路上的修改时，比一下校验值；对不上就请房主重发整份谱面。</summary>
        private static void MaybeCheckHash()
        {
            if (_pendingHash == null || State != CoopState.Synced || _awaitingResync || _hashCheckDisabled) return;
            if (_appliedSeq != _pendingHashSeq || ClientInbox.Count > 0 || _dirty || OutQueue.Count > 0 || _ownAcked < _authorSeq) return;
            if (!CoopEditor.CanApplyNow) return;
            string expected = _pendingHash;
            _pendingHash = null;
            if (CoopEditor.HashLevel() != expected) RequestResync("谱面校验不一致");
        }

        /// <summary>
        /// 刚把房主的整份谱面读进来：本地重新编码应该和房主发来的 JSON 一模一样。不一样说明两边读谱的环境不同
        /// （缺 DLC 事件、装了改谱面数据的 Mod 之类），把差异写进日志，并关掉自动校对，避免每次修改后都重新同步。
        /// </summary>
        private static void CheckReadsLikeHost(string hostJson, string when)
        {
            string local = CoopEditor.EncodeLevel();
            if (local == null || local == hostJson) return;
            int i = 0;
            while (i < local.Length && i < hostJson.Length && local[i] == hostJson[i]) i++;
            int start = Math.Max(0, i - 120);
            string Clip(string text) => start >= text.Length
                ? string.Empty
                : text.Substring(start, Math.Min(360, text.Length - start)).Replace((char)13, ' ').Replace((char)10, ' ');
            Main.Log("[Coop] " + when + "本地读出的谱面和房主的不一样（第 " + i + " 个字符起）。房主: …" + Clip(hostJson) + "… 本地: …" + Clip(local) + "…");
            _hashCheckDisabled = true;
            SetMessage("你的游戏读这张谱的结果和房主有细微差别（可能是缺少 DLC 或装了会改谱面数据的 Mod），已关闭自动校对。差异已写入日志。", error: true);
        }

        private static void RequestResync(string reason)
        {
            if (_awaitingResync || _hostPeer == null) return;
            _awaitingResync = true;
            Main.Log("[Coop] Requesting full resync: " + reason);
            _hostPeer.Send(CoopLane.Control, CoopMessage.ResyncRequest, new byte[0]);
        }

        /// <summary>客人收到纠正用的整份谱面：等能应用的时候换进编辑器。</summary>
        private static void ClientResyncSnapshot(CoopSnapshot snapshot)
        {
            _resync = snapshot;
        }

        private static void ApplyResync()
        {
            CoopSnapshot snapshot = _resync;
            _resync = null;
            _awaitingResync = false;
            if (snapshot.Seq < _appliedSeq) return; // 比现在还旧，不用
            if (!CoopEditor.ApplyLevelJson(snapshot.Json, out string error))
            {
                SetMessage("重新同步失败：" + error, error: true);
                return;
            }

            scnEditor editor = CoopEditor.Editor;
            if (!_model.AssignFromTable(editor.levelData, snapshot.IdTable, out string problem))
                Main.Log("[Coop] Resync id table mismatch: " + problem);
            _appliedSeq = snapshot.Seq;
            _dirty = false;
            CheckReadsLikeHost(snapshot.Json, "重新同步后");
            // 自己已经发出去、但还没回来的修改：回来时一定要重放（快照里不一定有它们）
            _foreignApplied++;
            EnsureFiles(snapshot.Manifest, LevelDir, _hostPeer);
            SetMessage("已和房主重新同步");
            CoopActivity.Add(_hostPeer?.SteamId ?? 0, HostName, "sync", "重新同步了整份谱面（和房主对齐）");
            Main.Log("[Coop] Resynced to seq " + snapshot.Seq);
        }
    }
}
