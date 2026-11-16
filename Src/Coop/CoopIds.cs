#nullable disable
using System.Runtime.CompilerServices;
using ADOFAI;
using HarmonyLib;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 事件、装饰的隐藏 ID（游戏本身没有 ID）。只记在内存里（按对象引用），不写进 .adofai。
    /// ID = 高 32 位 Lamport 计数 + 低 32 位本人的 Steam 账号号码：不同人生成的不会撞，后生成的总比先看到的大
    /// （砖块并发插入时靠这个排出一致的顺序）。会话开始时已有的东西用计数 0 + 序号。
    /// 游戏复制事件（撤销快照、粘贴）走 LevelEvent.Copy，补丁把 ID 带过去；同一个 ID 出现两次时，比对时给后一个换新 ID。
    /// </summary>
    internal static class CoopIds
    {
        /// <summary>第 0 块砖（起点）的固定 ID。</summary>
        public const long StartTile = 0;

        /// <summary>不挂在砖块上的东西（floor = -1 的装饰）。</summary>
        public const long NoFloor = -1;

        private static readonly ConditionalWeakTable<LevelEvent, Box> Table = new ConditionalWeakTable<LevelEvent, Box>();
        private static long _lamport;
        private static uint _tag = 1;

        public static void Reset(uint tag)
        {
            _lamport = 0;
            _tag = tag == 0 ? 1u : tag;
        }

        public static long NewId()
        {
            _lamport++;
            return (_lamport << 32) | _tag;
        }

        /// <summary>看到别人的 ID：本地计数至少要追上它，保证之后生成的比它大。</summary>
        public static void Observe(long id)
        {
            long counter = (long)((ulong)id >> 32);
            if (counter > _lamport) _lamport = counter;
        }

        /// <summary>按（计数, 账号）比较两个 ID：正数表示 a 比 b 新。</summary>
        public static int Compare(long a, long b)
        {
            return ((ulong)a).CompareTo((ulong)b);
        }

        public static long Get(LevelEvent levelEvent)
        {
            return levelEvent != null && Table.TryGetValue(levelEvent, out Box box) ? box.Id : 0;
        }

        public static void Set(LevelEvent levelEvent, long id)
        {
            if (levelEvent == null) return;
            Table.Remove(levelEvent);
            Table.Add(levelEvent, new Box(id));
        }

        private sealed class Box
        {
            public Box(long id)
            {
                Id = id;
            }

            public long Id { get; }
        }

        /// <summary>复制事件时把 ID 带过去（撤销快照里的对象和原对象 ID 相同，撤销换回来后还能对上）。</summary>
        [HarmonyPatch(typeof(LevelEvent), nameof(LevelEvent.Copy))]
        private static class CopyPatch
        {
            private static void Postfix(LevelEvent __instance, LevelEvent __result)
            {
                long id = Get(__instance);
                if (id != 0) Set(__result, id);
            }
        }
    }
}
