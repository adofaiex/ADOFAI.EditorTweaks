#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ADOFAI;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 单机自检增量同步：在当前谱面的三份拷贝上模拟"房主 + 两个客人同时改"——
    /// 两个客人各自随机改几下（插删砖、改角度、改 / 复制 / 删事件和装饰、调装饰顺序、改设置），比对出修改，
    /// 房主按顺序应用并"转发"，客人按同样的规则应用（自己的修改中间夹了别人的才重放），最后比较三份谱面编码是否完全一致。
    /// 只动拷贝，不碰编辑器里正在编辑的谱面。
    /// </summary>
    internal static class CoopSelfTest
    {
        public static string Run(LevelData source, int rounds)
        {
            var watch = Stopwatch.StartNew();
            var random = new Random(12345);
            CoopIds.Reset(7);

            LevelData hostData = source.Copy();
            var host = new CoopModel();
            host.BuildFresh(hostData);
            BinaryWriter writer = Packet.Writer(out MemoryStream table);
            host.WriteIdTable(writer);

            var guests = new[] { new Guest("A"), new Guest("B") };
            foreach (Guest guest in guests)
            {
                guest.Data = source.Copy();
                if (!guest.Model.AssignFromTable(guest.Data, table.ToArray(), out string problem)) return "自检失败：ID 表对不上（" + problem + "）";
            }

            int ops = 0;
            var diffWatch = new Stopwatch();
            var applyWatch = new Stopwatch();
            int diffs = 0, applies = 0;
            int mismatches = 0;
            int conflicts = 0;
            string firstMismatch = null;
            for (int round = 1; round <= rounds; round++)
            {
                // 两个客人基于同一个状态各自改（并发）
                var batches = new List<KeyValuePair<Guest, List<CoopOp>>>();
                foreach (Guest guest in guests)
                {
                    int edits = random.Next(1, 4);
                    for (int i = 0; i < edits; i++) Mutate(guest.Data, random);
                    diffWatch.Start();
                    List<CoopOp> diff = guest.Model.Diff(guest.Data);
                    diffWatch.Stop();
                    diffs++;
                    if (diff == null) return "自检失败：第 " + round + " 轮比对返回 null";
                    ops += diff.Count;
                    batches.Add(new KeyValuePair<Guest, List<CoopOp>>(guest, Roundtrip(diff)));
                }

                // 房主按随机顺序收到
                if (random.Next(2) == 0) batches.Reverse();
                applyWatch.Start();
                foreach (KeyValuePair<Guest, List<CoopOp>> batch in batches) host.Apply(hostData, batch.Value);
                applyWatch.Stop();
                applies += batches.Count;

                // 每个客人按房主的顺序应用：自己的那份如果前面夹了别人的，重放
                foreach (Guest guest in guests)
                {
                    bool foreignSeen = false;
                    foreach (KeyValuePair<Guest, List<CoopOp>> batch in batches)
                    {
                        if (batch.Key == guest)
                        {
                            if (foreignSeen) guest.Model.Apply(guest.Data, batch.Value);
                        }
                        else
                        {
                            guest.Model.Apply(guest.Data, batch.Value);
                            foreignSeen = true;
                        }
                    }
                }

                string expected = hostData.Encode();
                bool roundMismatch = false;
                foreach (Guest guest in guests)
                {
                    if (guest.Data.Encode() == expected) continue;
                    roundMismatch = true;
                    string where = Where(hostData, guest.Data);
                    Main.Log("[Coop] Self test mismatch round " + round + " guest " + guest.Name + " in " + where + "; ops: " + Describe(batches));
                    if (firstMismatch == null) firstMismatch = "第 " + round + " 轮客人 " + guest.Name + "（" + where + "）：" + FirstDifference(expected, guest.Data.Encode());
                    // 和真实流程一样：不一致就整份重发
                    guest.Data = hostData.Copy();
                    BinaryWriter resyncWriter = Packet.Writer(out MemoryStream resyncTable);
                    host.WriteIdTable(resyncWriter);
                    guest.Model.AssignFromTable(guest.Data, resyncTable.ToArray(), out _);
                }

                if (roundMismatch) mismatches++;
                if (batches.Count == 2 && Overlaps(batches[0].Value, batches[1].Value)) conflicts++;
            }

            watch.Stop();
            string summary = "同步自检：" + rounds + " 轮并发修改，共 " + ops + " 条修改，" + conflicts + " 轮两人改到同一处，" +
                             (mismatches == 0 ? "三份谱面全部一致" : mismatches + " 轮出现不一致（真实使用时会自动整份重发纠正）") +
                             "；比对一次平均 " + (diffWatch.ElapsedMilliseconds / Math.Max(1, diffs)) + " ms，应用一组平均 " +
                             (applyWatch.ElapsedMilliseconds / Math.Max(1, applies)) + " ms（谱面 " + source.levelEvents.Count + " 个事件、" +
                             source.decorations.Count + " 个装饰），总用时 " + watch.ElapsedMilliseconds + " ms";
            if (firstMismatch != null)
            {
                summary += "。首个差异：" + firstMismatch;
                Main.Log("[Coop] " + summary);
            }

            return summary;
        }

        private sealed class Guest
        {
            public Guest(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public LevelData Data;
            public readonly CoopModel Model = new CoopModel();
        }

        /// <summary>修改走一遍网络编码再解出来，和真实传输一样。</summary>
        private static List<CoopOp> Roundtrip(List<CoopOp> ops)
        {
            var batch = new CoopBatch { Ops = ops };
            return CoopBatch.Decode(batch.Encode()).Ops;
        }

        private static bool Overlaps(List<CoopOp> a, List<CoopOp> b)
        {
            var ids = new HashSet<long>();
            foreach (CoopOp op in a)
                if (op.Kind == CoopOpKind.Upsert || op.Kind == CoopOpKind.Remove) ids.Add(op.Id);
            foreach (CoopOp op in b)
                if ((op.Kind == CoopOpKind.Upsert || op.Kind == CoopOpKind.Remove) && ids.Contains(op.Id)) return true;
            return false;
        }

        // ---------------------------------------------------------------- 随机修改（模拟编辑器的行为）

        private static void Mutate(LevelData data, Random random)
        {
            List<LevelEvent> events = data.levelEvents;
            List<LevelEvent> decorations = data.decorations;
            int tiles = data.isOldLevel ? data.pathData.Length : data.angleData.Count;
            switch (random.Next(10))
            {
                case 0:
                case 1:
                {
                    // 插砖：后面的事件楼层 +1（编辑器 OffsetFloorIDsInEvents 的做法）
                    if (data.isOldLevel) break;
                    int index = random.Next(tiles + 1);
                    data.angleData.Insert(index, random.Next(8) * 45f);
                    Shift(events, decorations, index + 1, 1);
                    break;
                }

                case 2:
                {
                    // 删砖：这块砖上的事件一起删，后面的楼层 -1
                    if (data.isOldLevel || tiles < 3) break;
                    int index = random.Next(tiles);
                    events.RemoveAll(e => e.floor == index + 1);
                    data.angleData.RemoveAt(index);
                    Shift(events, decorations, index + 2, -1);
                    break;
                }

                case 3:
                    if (data.isOldLevel || tiles == 0) break;
                    data.angleData[random.Next(tiles)] = random.Next(8) * 45f;
                    break;
                case 4:
                    ChangeSomething(Pick(events, random), random);
                    break;
                case 5:
                    ChangeSomething(Pick(decorations, random), random);
                    break;
                case 6:
                {
                    // 复制一个事件到别的砖上（和粘贴一样走 Copy，会带着原件的 ID）
                    LevelEvent original = Pick(events, random);
                    if (original == null || tiles == 0) break;
                    LevelEvent copy = original.Copy();
                    copy.floor = random.Next(1, tiles + 1);
                    events.Add(copy);
                    break;
                }

                case 7:
                    if (events.Count > 0) events.RemoveAt(random.Next(events.Count));
                    else if (decorations.Count > 0) decorations.RemoveAt(random.Next(decorations.Count));
                    break;
                case 8:
                    if (decorations.Count >= 2)
                    {
                        int a = random.Next(decorations.Count), b = random.Next(decorations.Count);
                        (decorations[a], decorations[b]) = (decorations[b], decorations[a]);
                    }
                    else if (decorations.Count == 1)
                    {
                        decorations.Add(decorations[0].Copy());
                    }

                    break;
                default:
                    LevelEvent[] synced = CoopModel.SyncedSettings(data);
                    ChangeSomething(synced[random.Next(synced.Length)], random);
                    break;
            }
        }

        private static void Shift(List<LevelEvent> events, List<LevelEvent> decorations, int fromFloor, int offset)
        {
            foreach (LevelEvent e in events)
                if (e.floor >= fromFloor) e.floor += offset;
            foreach (LevelEvent d in decorations)
                if (d.floor >= fromFloor) d.floor += offset;
        }

        /// <summary>随便改一个数值属性（找不到数值就切换启用状态）。</summary>
        private static void ChangeSomething(LevelEvent levelEvent, Random random)
        {
            if (levelEvent == null) return;
            Dictionary<string, object> data = levelEvent.GetData();
            var keys = new List<string>();
            foreach (KeyValuePair<string, object> pair in data)
            {
                if (!(pair.Value is float || pair.Value is int)) continue;
                if (levelEvent.disabled != null && levelEvent.disabled.TryGetValue(pair.Key, out bool off) && off) continue;
                if (levelEvent.info.propertiesInfo.TryGetValue(pair.Key, out PropertyInfo info) && info.encode && pair.Key != "floor") keys.Add(pair.Key);
            }

            if (keys.Count == 0)
            {
                levelEvent.active = !levelEvent.active;
                return;
            }

            string key = keys[random.Next(keys.Count)];
            if (data[key] is float f) data[key] = f + random.Next(1, 5);
            else if (data[key] is int i) data[key] = i + random.Next(1, 3);
        }

        private static LevelEvent Pick(List<LevelEvent> list, Random random)
        {
            return list.Count == 0 ? null : list[random.Next(list.Count)];
        }

        private static string Where(LevelData expected, LevelData actual)
        {
            var parts = new List<string>();
            if (string.Join(",", expected.angleData) != string.Join(",", actual.angleData)) parts.Add("砖块");
            if (Section(expected.levelEvents) != Section(actual.levelEvents)) parts.Add("事件");
            if (Section(expected.decorations) != Section(actual.decorations)) parts.Add("装饰");
            var a = new List<LevelEvent>(CoopModel.SyncedSettings(expected));
            var b = new List<LevelEvent>(CoopModel.SyncedSettings(actual));
            for (int i = 0; i < a.Count; i++)
            {
                string x = CoopModel.EncodeSettings(a[i]), y = CoopModel.EncodeSettings(b[i]);
                if (x != y) parts.Add("设置 " + a[i].eventType + "：" + FirstDifference(x, y));
            }
            return parts.Count == 0 ? "顺序" : string.Join("、", parts);
        }

        private static string Section(List<LevelEvent> list)
        {
            var items = new List<string>();
            foreach (LevelEvent e in list) items.Add(e.floor + ":" + CoopModel.Encode(e));
            items.Sort(StringComparer.Ordinal);
            return string.Join(Environment.NewLine, items);
        }

        private static string Describe(List<KeyValuePair<Guest, List<CoopOp>>> batches)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<Guest, List<CoopOp>> batch in batches)
            {
                var kinds = new Dictionary<CoopOpKind, int>();
                foreach (CoopOp op in batch.Value) kinds[op.Kind] = kinds.TryGetValue(op.Kind, out int n) ? n + 1 : 1;
                var text = new List<string>();
                foreach (KeyValuePair<CoopOpKind, int> pair in kinds) text.Add(pair.Key + "x" + pair.Value);
                parts.Add(batch.Key.Name + "[" + string.Join(" ", text) + "]");
            }

            return string.Join(" then ", parts);
        }

        private static string FirstDifference(string expected, string actual)
        {
            int i = 0;
            while (i < expected.Length && i < actual.Length && expected[i] == actual[i]) i++;
            int start = Math.Max(0, i - 60);
            string Clip(string text) => text.Substring(start, Math.Min(140, text.Length - start)).Replace("\n", " ");
            return "期望 …" + Clip(expected) + "… 实际 …" + Clip(actual) + "…";
        }
    }
}
