#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ADOFAI;
using GDMiniJSON;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// "上次同步时谱面长什么样"的记录：砖块（ID + 角度）、每个事件 / 装饰（ID → 内容 JSON + 所在砖块 ID）、装饰顺序、各类设置。
    /// <para>本地改了谱面：拿编辑器里的谱面和它比，得出一组修改（<see cref="Diff"/>），再把记录更新成现在的样子。</para>
    /// <para>收到别人的修改：同时改编辑器里的谱面和这份记录（<see cref="Apply"/>）。</para>
    /// 事件的所在砖块用砖块 ID 而不是序号记录，所以别人在前面插了砖，事件也不会错位。
    /// </summary>
    internal sealed class CoopModel
    {
        public readonly List<long> TileIds = new List<long>();
        public readonly List<string> TileValues = new List<string>();
        public readonly Dictionary<long, Item> Items = new Dictionary<long, Item>();
        public readonly List<long> DecorationOrder = new List<long>();
        public readonly Dictionary<string, string> Settings = new Dictionary<string, string>();
        public bool OldLevel;

        /// <summary>删掉的砖 → 它被删时前面那块砖。别人插在已删砖后面的砖，顺着这里找到还在的前一块，插到那后面。</summary>
        private readonly Dictionary<long, long> _tombstones = new Dictionary<long, long>();

        public sealed class Item
        {
            public LevelEvent Obj;
            public string Json;
            public long FloorTile;
            public bool Decoration;

            /// <summary>所在的砖已经被删了，暂时从谱面里拿出来（不算删除：之后的修改把它挪到还在的砖上就放回去）。</summary>
            public bool Parked;

            /// <summary>拿出来时后面那个对象的 ID（放回去时插在它前面，保持原来的顺序）。</summary>
            public long ParkedBefore;
        }

        /// <summary>Apply 之后编辑器需要刷新哪些东西。</summary>
        public sealed class ApplyResult
        {
            public bool TilesChanged;
            public bool EventsChanged;
            public bool DecorationsRebuilt;
            public bool SettingsChanged;
            public readonly List<LevelEvent> DecorationsChanged = new List<LevelEvent>();
            public readonly HashSet<LevelEvent> Removed = new HashSet<LevelEvent>();
            public readonly HashSet<LevelEvent> Touched = new HashSet<LevelEvent>();

            public bool Any => TilesChanged || EventsChanged || DecorationsRebuilt || SettingsChanged || DecorationsChanged.Count > 0;
        }

        // ================================================================ 建立

        /// <summary>房主开房时：给现有的砖块、事件、装饰编上初始 ID。</summary>
        public void BuildFresh(LevelData data)
        {
            Clear();
            OldLevel = data.isOldLevel;
            List<string> values = TileValuesOf(data);
            for (int i = 0; i < values.Count; i++)
            {
                TileIds.Add(i + 1);
                TileValues.Add(values[i]);
            }

            long next = 1;
            foreach (LevelEvent levelEvent in data.levelEvents)
            {
                CoopIds.Set(levelEvent, next);
                AddItem(levelEvent, next++, decoration: false);
            }

            foreach (LevelEvent decoration in data.decorations)
            {
                CoopIds.Set(decoration, next);
                AddItem(decoration, next++, decoration: true);
                DecorationOrder.Add(CoopIds.Get(decoration));
            }

            CaptureSettings(data);
        }

        /// <summary>写出 ID 表（整份谱面快照里带着，客人打开谱面后按内容把 ID 对上）。</summary>
        public void WriteIdTable(BinaryWriter writer)
        {
            writer.Write(OldLevel);
            writer.Write(TileIds.Count);
            foreach (long id in TileIds) writer.Write(id);
            var live = new List<KeyValuePair<long, Item>>();
            foreach (KeyValuePair<long, Item> pair in Items)
                if (!pair.Value.Parked) live.Add(pair);
            writer.Write(live.Count);
            foreach (KeyValuePair<long, Item> pair in live)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value.Decoration);
                writer.Write(Fingerprint(pair.Value.Json, pair.Value.FloorTile));
            }
        }

        /// <summary>
        /// 客人打开房主的谱面后：按 ID 表给本地对象编 ID。砖块按顺序对应；事件 / 装饰按内容指纹对应（内容完全一样的几个谁拿哪个 ID 都行）。
        /// 返回有没有对不上的（对不上说明两边谱面不一样，之后的校验会触发整份重发）。
        /// </summary>
        public bool AssignFromTable(LevelData data, byte[] table, out string problem)
        {
            problem = null;
            Clear();
            OldLevel = data.isOldLevel;
            BinaryReader reader = Packet.Reader(table);
            bool oldLevel = reader.ReadBoolean();
            int tiles = reader.ReadInt32();
            var tileIds = new List<long>(tiles);
            for (int i = 0; i < tiles; i++) tileIds.Add(reader.ReadInt64());
            var lookup = new Dictionary<string, Queue<long>>();
            int items = reader.ReadInt32();
            for (int i = 0; i < items; i++)
            {
                long id = reader.ReadInt64();
                bool decoration = reader.ReadBoolean();
                string key = (decoration ? "d" : "e") + reader.ReadString();
                if (!lookup.TryGetValue(key, out Queue<long> queue)) lookup[key] = queue = new Queue<long>();
                queue.Enqueue(id);
                CoopIds.Observe(id);
            }

            List<string> values = TileValuesOf(data);
            if (oldLevel != data.isOldLevel || values.Count != tileIds.Count)
            {
                problem = "砖块数量对不上（本地 " + values.Count + "，房主 " + tileIds.Count + "）";
                for (int i = 0; i < values.Count; i++) tileIds.Add(CoopIds.NewId());
            }

            for (int i = 0; i < values.Count; i++)
            {
                TileIds.Add(tileIds[i]);
                TileValues.Add(values[i]);
                CoopIds.Observe(tileIds[i]);
            }

            int unmatched = 0;
            void Match(LevelEvent levelEvent, bool decoration)
            {
                string json = Encode(levelEvent);
                long floorTile = TileOf(levelEvent.floor);
                string key = (decoration ? "d" : "e") + Fingerprint(json, floorTile);
                long id;
                if (lookup.TryGetValue(key, out Queue<long> queue) && queue.Count > 0)
                {
                    id = queue.Dequeue();
                }
                else
                {
                    id = CoopIds.NewId();
                    unmatched++;
                }

                CoopIds.Set(levelEvent, id);
                Items[id] = new Item { Obj = levelEvent, Json = json, FloorTile = floorTile, Decoration = decoration };
            }

            foreach (LevelEvent levelEvent in data.levelEvents) Match(levelEvent, false);
            foreach (LevelEvent decoration in data.decorations)
            {
                Match(decoration, true);
                DecorationOrder.Add(CoopIds.Get(decoration));
            }

            CaptureSettings(data);
            if (unmatched > 0 && problem == null) problem = unmatched + " 个事件 / 装饰和房主的对不上";
            return problem == null;
        }

        private void Clear()
        {
            TileIds.Clear();
            TileValues.Clear();
            Items.Clear();
            DecorationOrder.Clear();
            Settings.Clear();
            _tombstones.Clear();
        }

        private void AddItem(LevelEvent levelEvent, long id, bool decoration)
        {
            Items[id] = new Item { Obj = levelEvent, Json = Encode(levelEvent), FloorTile = TileOf(levelEvent.floor), Decoration = decoration };
        }

        private void CaptureSettings(LevelData data)
        {
            foreach (LevelEvent settings in data.settings) Settings[settings.eventType.ToString()] = EncodeSettings(settings);
        }

        // ================================================================ 本地修改 → 一组修改

        /// <summary>
        /// 编辑器里的谱面和记录比，得出修改，并把记录更新成现在的样子。
        /// 返回 null 表示没法用修改描述（比如谱面格式变了），需要整份重发。
        /// </summary>
        public List<CoopOp> Diff(LevelData data)
        {
            if (data.isOldLevel != OldLevel) return null;
            var ops = new List<CoopOp>();
            DiffTiles(data, ops);

            var seen = new HashSet<long>();
            var upserts = new List<CoopOp>();
            DiffList(data.levelEvents, decoration: false, seen, upserts);
            DiffList(data.decorations, decoration: true, seen, upserts);
            ops.AddRange(upserts);

            var removed = new List<long>();
            foreach (KeyValuePair<long, Item> pair in Items)
                if (!seen.Contains(pair.Key) && !pair.Value.Parked) removed.Add(pair.Key);
            foreach (long id in removed)
            {
                ops.Add(new CoopOp { Kind = CoopOpKind.Remove, Id = id, Decoration = Items[id].Decoration });
                Items.Remove(id);
            }

            // 装饰顺序：按收到的人会怎么插入模拟一遍，结果和现在不一样才发整个顺序
            var order = new List<long>(data.decorations.Count);
            foreach (LevelEvent decoration in data.decorations) order.Add(CoopIds.Get(decoration));
            var simulated = new List<long>(DecorationOrder);
            simulated.RemoveAll(id => !Items.ContainsKey(id));
            foreach (CoopOp op in upserts)
            {
                if (!op.Decoration || !op.Created) continue;
                int at = op.Anchor == 0 ? -1 : simulated.IndexOf(op.Anchor);
                if (at < 0) simulated.Add(op.Id);
                else simulated.Insert(at, op.Id);
            }

            if (!SameSequence(simulated, order)) ops.Add(new CoopOp { Kind = CoopOpKind.DecorationOrder, Ids = new List<long>(order) });
            DecorationOrder.Clear();
            DecorationOrder.AddRange(order);

            foreach (LevelEvent settings in data.settings)
            {
                string type = settings.eventType.ToString();
                string json = EncodeSettings(settings);
                if (Settings.TryGetValue(type, out string old) && old == json) continue;
                Settings[type] = json;
                ops.Add(new CoopOp { Kind = CoopOpKind.Settings, SettingsType = type, Json = json });
            }

            return ops;
        }

        private void DiffTiles(LevelData data, List<CoopOp> ops)
        {
            List<string> current = TileValuesOf(data);
            if (current.Count == TileValues.Count)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    if (current[i] == TileValues[i]) continue;
                    TileValues[i] = current[i];
                    ops.Add(new CoopOp { Kind = CoopOpKind.TileSet, Id = TileIds[i], Value = current[i] });
                }

                return;
            }

            // 长度变了：找出前后相同的部分，中间那段算删掉旧的、插入新的
            int prefix = 0;
            int max = Math.Min(current.Count, TileValues.Count);
            while (prefix < max && current[prefix] == TileValues[prefix]) prefix++;
            int suffix = 0;
            while (suffix < max - prefix && current[current.Count - 1 - suffix] == TileValues[TileValues.Count - 1 - suffix]) suffix++;

            // 纯插入 / 纯删除时，连续相同角度里到底插 / 删的是哪一块看不出来：用事件跟着砖块挪动的情况来判断
            if (current.Count != TileValues.Count && (prefix + suffix == current.Count || prefix + suffix == TileValues.Count))
                prefix = ResolveSplicePosition(data, current, prefix, suffix, out suffix);

            int deleteCount = TileValues.Count - prefix - suffix;
            if (deleteCount > 0)
            {
                long before = prefix == 0 ? CoopIds.StartTile : TileIds[prefix - 1];
                for (int i = 0; i < deleteCount; i++) _tombstones[TileIds[prefix + i]] = before;
                ops.Add(new CoopOp { Kind = CoopOpKind.TileDelete, Ids = TileIds.GetRange(prefix, deleteCount) });
                TileIds.RemoveRange(prefix, deleteCount);
                TileValues.RemoveRange(prefix, deleteCount);
            }

            int insertCount = current.Count - prefix - suffix;
            if (insertCount > 0)
            {
                var ids = new List<long>(insertCount);
                for (int i = 0; i < insertCount; i++) ids.Add(CoopIds.NewId());
                List<string> values = current.GetRange(prefix, insertCount);
                ops.Add(new CoopOp
                {
                    Kind = CoopOpKind.TileInsert,
                    Anchor = prefix == 0 ? CoopIds.StartTile : TileIds[prefix - 1],
                    Ids = ids,
                    Values = values,
                });
                TileIds.InsertRange(prefix, ids);
                TileValues.InsertRange(prefix, values);
            }
        }

        /// <summary>
        /// 在一串相同角度里插入或删除了 k 块砖，可能的位置是一个区间。编辑器插删砖时会把后面的事件楼层一起挪，
        /// 所以"楼层没变的事件"说明插删点在它后面，"楼层挪了 k 的事件"说明在它前面；取让最多事件对得上的位置。
        /// </summary>
        private int ResolveSplicePosition(LevelData data, List<string> current, int prefix, int suffix, out int newSuffix)
        {
            newSuffix = suffix;
            int oldCount = TileValues.Count;
            bool deletion = oldCount > current.Count;
            int k = Math.Abs(oldCount - current.Count);
            int shorter = Math.Min(oldCount, current.Count);

            // 不限长度的公共后缀，得到可能位置的下限
            int fullSuffix = 0;
            while (fullSuffix < shorter && current[current.Count - 1 - fullSuffix] == TileValues[oldCount - 1 - fullSuffix]) fullSuffix++;
            int low = Math.Max(0, shorter - fullSuffix);
            int high = prefix;
            if (low >= high) return prefix;

            var oldIndex = new Dictionary<long, int>(oldCount);
            for (int i = 0; i < oldCount; i++) oldIndex[TileIds[i]] = i;

            // votesAfter[c]：要求插删点 >= c 的票；votesBefore[c]：要求插删点 <= c 的票
            var atLeast = new int[high + 2];
            var atMost = new int[high + 2];
            void Vote(LevelEvent levelEvent)
            {
                long id = CoopIds.Get(levelEvent);
                if (id == 0 || !Items.TryGetValue(id, out Item item) || levelEvent.floor < 1) return;
                if (!oldIndex.TryGetValue(item.FloorTile, out int o)) return;
                int t = levelEvent.floor - 1;
                if (o == t)
                {
                    // 没挪：插删点在它后面
                    int a = Math.Min(t + 1, high + 1);
                    atLeast[a]++;
                }
                else if (deletion ? o == t + k : o == t - k)
                {
                    // 挪了：插删点在它前面
                    int b = deletion ? t : t - k;
                    if (b >= 0) atMost[Math.Min(b, high + 1)]++;
                }
            }

            foreach (LevelEvent levelEvent in data.levelEvents) Vote(levelEvent);
            foreach (LevelEvent decoration in data.decorations) Vote(decoration);

            // 扫一遍：score(c) = Σ atLeast[a≤c] + Σ atMost[b≥c]
            int totalAtMost = 0;
            for (int i = 0; i <= high + 1; i++) totalAtMost += atMost[i];
            int best = high, bestScore = -1, prefixAtLeast = 0, suffixAtMost = totalAtMost;
            for (int c = 0; c <= high; c++)
            {
                prefixAtLeast += atLeast[c];
                if (c > 0) suffixAtMost -= atMost[c - 1];
                if (c < low) continue;
                int score = prefixAtLeast + suffixAtMost;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            newSuffix = shorter - best;
            return best;
        }

        private void DiffList(List<LevelEvent> list, bool decoration, HashSet<long> seen, List<CoopOp> ops)
        {
            for (int i = 0; i < list.Count; i++)
            {
                LevelEvent levelEvent = list[i];
                long id = CoopIds.Get(levelEvent);
                if (id == 0 || seen.Contains(id))
                {
                    // 新建的，或者是复制出来的（和原件同一个 ID）
                    id = CoopIds.NewId();
                    CoopIds.Set(levelEvent, id);
                }

                seen.Add(id);
                string json = Encode(levelEvent);
                long floorTile = TileOf(levelEvent.floor);
                if (Items.TryGetValue(id, out Item item) && item.Decoration == decoration)
                {
                    item.Obj = levelEvent;
                    item.Parked = false;
                    if (item.Json == json && item.FloorTile == floorTile) continue;
                    item.Json = json;
                    item.FloorTile = floorTile;
                    ops.Add(new CoopOp { Kind = CoopOpKind.Upsert, Id = id, Decoration = decoration, FloorTile = floorTile, Json = json });
                    continue;
                }

                Items[id] = new Item { Obj = levelEvent, Json = json, FloorTile = floorTile, Decoration = decoration };
                ops.Add(new CoopOp
                {
                    Kind = CoopOpKind.Upsert,
                    Id = id,
                    Decoration = decoration,
                    Created = true,
                    FloorTile = floorTile,
                    Json = json,
                    // 插在后一个对象前面（用 ID 定位，两边列表顺序不完全一样也能放到同一个相对位置）
                    Anchor = i + 1 < list.Count ? EnsureId(list[i + 1]) : 0,
                });
            }
        }

        /// <summary>给还没 ID 的对象先编一个（Anchor 引用到了列表里后面的新对象）。</summary>
        private static long EnsureId(LevelEvent levelEvent)
        {
            long id = CoopIds.Get(levelEvent);
            if (id != 0) return id;
            id = CoopIds.NewId();
            CoopIds.Set(levelEvent, id);
            return id;
        }

        // ================================================================ 别人的修改 → 应用

        /// <summary>把一组修改同时应用到编辑器里的谱面和这份记录上。</summary>
        public ApplyResult Apply(LevelData data, List<CoopOp> ops)
        {
            var result = new ApplyResult();
            List<LevelEvent> events = data.levelEvents;
            List<LevelEvent> decorations = data.decorations;

            foreach (CoopOp op in ops)
            {
                switch (op.Kind)
                {
                    case CoopOpKind.TileInsert:
                        ApplyTileInsert(data, op, result);
                        break;
                    case CoopOpKind.TileDelete:
                        foreach (long id in op.Ids)
                        {
                            int index = TileIds.IndexOf(id);
                            if (index < 0) continue;
                            _tombstones[id] = index == 0 ? CoopIds.StartTile : TileIds[index - 1];
                            TileIds.RemoveAt(index);
                            TileValues.RemoveAt(index);
                            RemoveTile(data, index);
                            result.TilesChanged = true;
                        }

                        break;
                    case CoopOpKind.TileSet:
                    {
                        int index = TileIds.IndexOf(op.Id);
                        if (index < 0 || TileValues[index] == op.Value) break;
                        TileValues[index] = op.Value;
                        SetTile(data, index, op.Value);
                        result.TilesChanged = true;
                        break;
                    }

                    case CoopOpKind.Upsert:
                        ApplyUpsert(op, events, decorations, result);
                        break;
                    case CoopOpKind.Remove:
                        if (!Items.TryGetValue(op.Id, out Item gone)) break;
                        Items.Remove(op.Id);
                        if (gone.Parked) break;
                        (gone.Decoration ? decorations : events).Remove(gone.Obj);
                        result.Removed.Add(gone.Obj);
                        if (gone.Decoration) result.DecorationsRebuilt = true;
                        else result.EventsChanged = true;
                        break;
                    case CoopOpKind.DecorationOrder:
                        ApplyDecorationOrder(decorations, op.Ids, result);
                        break;
                    case CoopOpKind.Settings:
                        ApplySettings(data, op, result);
                        break;
                }
            }

            // 砖块变了：按砖块 ID 重新算每个事件 / 装饰的 floor；所在砖块已经没了的事件一起删掉（每个人都这么做，结果一致）
            if (result.TilesChanged || result.EventsChanged || result.DecorationsRebuilt || result.DecorationsChanged.Count > 0)
                RelinkFloors(events, decorations, result);

            DecorationOrder.Clear();
            foreach (LevelEvent decoration in decorations) DecorationOrder.Add(CoopIds.Get(decoration));
            return result;
        }

        private void ApplyTileInsert(LevelData data, CoopOp op, ApplyResult result)
        {
            long anchor = op.Anchor;
            for (int k = 0; k < op.Ids.Count; k++)
            {
                long id = op.Ids[k];
                CoopIds.Observe(id);
                if (TileIds.Contains(id))
                {
                    anchor = id;
                    continue;
                }

                int position = PositionAfter(anchor);
                // 同一个位置有别人同时插入的砖：比它新的排在前面（每个人按同一规则排，顺序一致）
                while (position < TileIds.Count && CoopIds.Compare(TileIds[position], id) > 0 && ((ulong)TileIds[position] >> 32) > 0)
                    position++;
                TileIds.Insert(position, id);
                TileValues.Insert(position, op.Values[k]);
                InsertTile(data, position, op.Values[k]);
                result.TilesChanged = true;
                anchor = id;
            }
        }

        /// <summary>插在 anchor 后面的位置；anchor 已经被删了就顺着墓碑找它前面还在的砖。</summary>
        private int PositionAfter(long anchor)
        {
            for (int guard = 0; guard < 100000; guard++)
            {
                if (anchor == CoopIds.StartTile) return 0;
                int index = TileIds.IndexOf(anchor);
                if (index >= 0) return index + 1;
                if (!_tombstones.TryGetValue(anchor, out anchor)) break;
            }

            return TileIds.Count;
        }

        private void ApplyUpsert(CoopOp op, List<LevelEvent> events, List<LevelEvent> decorations, ApplyResult result)
        {
            CoopIds.Observe(op.Id);
            if (!(Json.Deserialize(op.Json) is Dictionary<string, object> dict)) return;
            int floor = FloorOf(op.FloorTile);
            if (floor >= 0) dict["floor"] = floor;
            List<LevelEvent> list = op.Decoration ? decorations : events;

            if (Items.TryGetValue(op.Id, out Item item))
            {
                if (item.Json == op.Json && item.FloorTile == op.FloorTile && item.Decoration == op.Decoration) return;
                if (item.Decoration != op.Decoration && !item.Parked)
                {
                    (item.Decoration ? decorations : events).Remove(item.Obj);
                    list.Add(item.Obj);
                    result.DecorationsRebuilt = true;
                }

                item.Decoration = op.Decoration;

                // 原地改（对象不换，选中状态、装饰物体引用都还有效）
                if (item.Obj.Decode(dict) != LevelEvent.DecodeResult.Success) return;
                item.Obj.floor = floor;
                item.FloorTile = op.FloorTile;
                item.Json = Encode(item.Obj);
                if (item.Parked && (floor >= 0 || op.FloorTile == CoopIds.NoFloor))
                {
                    // 被挪到了还在的砖上：放回谱面
                    item.Parked = false;
                    int back = item.ParkedBefore == 0 ? -1 : IndexOfId(list, item.ParkedBefore);
                    if (back < 0) list.Add(item.Obj);
                    else list.Insert(back, item.Obj);
                    if (op.Decoration) result.DecorationsRebuilt = true;
                    else result.EventsChanged = true;
                }
                result.Touched.Add(item.Obj);
                if (op.Decoration) result.DecorationsChanged.Add(item.Obj);
                else result.EventsChanged = true;
                return;
            }

            // 别人改的是一个已经被删掉的对象：丢弃（新建的才建出来）
            if (!op.Created) return;
            var created = new LevelEvent(dict);
            if (created.GetData() == null || created.eventType == LevelEventType.None) return;
            created.floor = floor;
            CoopIds.Set(created, op.Id);
            int at = op.Anchor == 0 ? -1 : IndexOfId(list, op.Anchor);
            if (at < 0) list.Add(created);
            else list.Insert(at, created);
            Items[op.Id] = new Item { Obj = created, Json = Encode(created), FloorTile = op.FloorTile, Decoration = op.Decoration };
            result.Touched.Add(created);
            if (op.Decoration) result.DecorationsRebuilt = true;
            else result.EventsChanged = true;
        }

        private void ApplyDecorationOrder(List<LevelEvent> decorations, List<long> ids, ApplyResult result)
        {
            var byId = new Dictionary<long, LevelEvent>();
            foreach (LevelEvent decoration in decorations) byId[CoopIds.Get(decoration)] = decoration;
            var ordered = new List<LevelEvent>(decorations.Count);
            foreach (long id in ids)
            {
                if (!byId.TryGetValue(id, out LevelEvent decoration)) continue;
                ordered.Add(decoration);
                byId.Remove(id);
            }

            foreach (LevelEvent decoration in decorations)
                if (byId.ContainsKey(CoopIds.Get(decoration))) ordered.Add(decoration);

            bool same = true;
            for (int i = 0; i < ordered.Count && same; i++) same = ReferenceEquals(ordered[i], decorations[i]);
            if (same) return;
            decorations.Clear();
            decorations.AddRange(ordered);
            result.DecorationsRebuilt = true;
        }

        private void ApplySettings(LevelData data, CoopOp op, ApplyResult result)
        {
            if (Settings.TryGetValue(op.SettingsType, out string old) && old == op.Json) return;
            if (!(Json.Deserialize(op.Json) is Dictionary<string, object> dict)) return;
            foreach (LevelEvent settings in data.settings)
            {
                if (settings.eventType.ToString() != op.SettingsType) continue;
                settings.Decode(dict, op.SettingsType, isGlobal: true);
                Settings[op.SettingsType] = EncodeSettings(settings);
                result.SettingsChanged = true;
                return;
            }
        }

        private void RelinkFloors(List<LevelEvent> events, List<LevelEvent> decorations, ApplyResult result)
        {
            var index = new Dictionary<long, int>(TileIds.Count);
            for (int i = 0; i < TileIds.Count; i++) index[TileIds[i]] = i + 1;
            var orphans = new List<long>();
            foreach (KeyValuePair<long, Item> pair in Items)
            {
                Item item = pair.Value;
                if (item.Parked) continue;
                int floor;
                if (item.FloorTile == CoopIds.NoFloor) floor = -1;
                else if (item.FloorTile == CoopIds.StartTile) floor = 0;
                else if (!index.TryGetValue(item.FloorTile, out floor))
                {
                    orphans.Add(pair.Key);
                    continue;
                }

                if (item.Obj.floor == floor) continue;
                item.Obj.floor = floor;
                if (item.Decoration) result.DecorationsChanged.Add(item.Obj);
                else result.EventsChanged = true;
            }

            foreach (long id in orphans)
            {
                Item item = Items[id];
                item.Parked = true;
                List<LevelEvent> list = item.Decoration ? decorations : events;
                int at = list.IndexOf(item.Obj);
                item.ParkedBefore = at >= 0 && at + 1 < list.Count ? CoopIds.Get(list[at + 1]) : 0;
                list.Remove(item.Obj);
                result.Removed.Add(item.Obj);
                if (item.Decoration) result.DecorationsRebuilt = true;
                else result.EventsChanged = true;
            }
        }

        // ================================================================ 小工具

        /// <summary>楼层序号 → 砖块 ID（0 = 起点，-1 = 不挂砖块）。</summary>
        public long TileOf(int floor)
        {
            if (floor < 0) return CoopIds.NoFloor;
            if (floor == 0 || TileIds.Count == 0) return CoopIds.StartTile;
            return TileIds[Math.Min(floor, TileIds.Count) - 1];
        }

        /// <summary>砖块 ID → 楼层序号（砖块不存在返回 -1）。</summary>
        public int FloorOf(long tile)
        {
            if (tile == CoopIds.NoFloor) return -1;
            if (tile == CoopIds.StartTile) return 0;
            int index = TileIds.IndexOf(tile);
            return index < 0 ? -1 : index + 1;
        }

        /// <summary>事件内容（去掉 floor，所在砖块另外用砖块 ID 记），用来比较和传输。</summary>
        public static string Encode(LevelEvent levelEvent)
        {
            Dictionary<string, object> dict = levelEvent.Encode();
            dict.Remove("floor");
            return Json.Serialize(dict);
        }

        public static string EncodeSettings(LevelEvent settings)
        {
            return Json.Serialize(settings.Encode(settings: true));
        }

        private static string Fingerprint(string json, long floorTile)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return FileHashes.ToHex(md5.ComputeHash(Encoding.UTF8.GetBytes(floorTile.ToString(CultureInfo.InvariantCulture) + "|" + json)));
        }

        public static List<string> TileValuesOf(LevelData data)
        {
            var values = new List<string>();
            if (data.isOldLevel)
            {
                foreach (char c in data.pathData ?? string.Empty) values.Add(c.ToString());
            }
            else
            {
                foreach (float angle in data.angleData) values.Add(angle.ToString("R", CultureInfo.InvariantCulture));
            }

            return values;
        }

        private void InsertTile(LevelData data, int index, string value)
        {
            if (OldLevel) data.pathData = data.pathData.Insert(index, value);
            else data.angleData.Insert(index, float.Parse(value, CultureInfo.InvariantCulture));
        }

        private void RemoveTile(LevelData data, int index)
        {
            if (OldLevel) data.pathData = data.pathData.Remove(index, 1);
            else data.angleData.RemoveAt(index);
        }

        private void SetTile(LevelData data, int index, string value)
        {
            if (OldLevel) data.pathData = data.pathData.Remove(index, 1).Insert(index, value);
            else data.angleData[index] = float.Parse(value, CultureInfo.InvariantCulture);
        }

        private static int IndexOfId(List<LevelEvent> list, long id)
        {
            for (int i = 0; i < list.Count; i++)
                if (CoopIds.Get(list[i]) == id) return i;
            return -1;
        }

        private static bool SameSequence(List<long> a, List<long> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
