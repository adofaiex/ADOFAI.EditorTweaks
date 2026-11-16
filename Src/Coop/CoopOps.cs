#nullable disable
using System.Collections.Generic;
using System.IO;

namespace ADOFAI.EditorTweaks.Coop
{
    internal enum CoopOpKind : byte
    {
        /// <summary>在某块砖后面插入一串砖（ID + 角度）。</summary>
        TileInsert = 1,

        /// <summary>删掉一些砖。</summary>
        TileDelete = 2,

        /// <summary>改一块砖的角度。</summary>
        TileSet = 3,

        /// <summary>新增或修改一个事件 / 装饰（整条内容，所在砖块用砖块 ID 表示）。</summary>
        Upsert = 4,

        /// <summary>删掉一个事件 / 装饰。</summary>
        Remove = 5,

        /// <summary>装饰的前后顺序（拖动排序时）。</summary>
        DecorationOrder = 6,

        /// <summary>某一类关卡设置的整组内容。</summary>
        Settings = 7,
    }

    /// <summary>
    /// 一条修改。全部是"把某样东西设成某个值"这种绝对的写法（砖块插入用 ID 去重），
    /// 所以按房主的顺序重放一遍总能得到同样的结果，作者自己再收到也不会重复生效。
    /// </summary>
    internal sealed class CoopOp
    {
        public CoopOpKind Kind;

        /// <summary>Upsert / Remove / TileSet 的对象 ID。</summary>
        public long Id;

        /// <summary>TileInsert：插在哪块砖后面；Upsert（新建时）：插在哪个对象前面（0 = 末尾）。</summary>
        public long Anchor;

        /// <summary>TileInsert / TileDelete / DecorationOrder 的 ID 列表。</summary>
        public List<long> Ids;

        /// <summary>TileInsert 的角度；TileSet 用 Value。</summary>
        public List<string> Values;

        public string Value;

        /// <summary>Upsert：是装饰（否则是轨道事件）。</summary>
        public bool Decoration;

        /// <summary>Upsert：是新建的（修改一个已经被别人删掉的对象时丢弃，新建的才会建出来）。</summary>
        public bool Created;

        /// <summary>Upsert：所在砖块的 ID（StartTile / NoFloor / 砖块 ID）。</summary>
        public long FloorTile;

        /// <summary>Upsert：事件内容（去掉 floor 的 JSON）；Settings：设置内容。</summary>
        public string Json;

        /// <summary>Settings：哪一类设置（SongSettings 之类）。</summary>
        public string SettingsType;

        public void Write(BinaryWriter writer)
        {
            writer.Write((byte)Kind);
            switch (Kind)
            {
                case CoopOpKind.TileInsert:
                    writer.Write(Anchor);
                    WriteIds(writer, Ids);
                    foreach (string value in Values) writer.Write(value);
                    break;
                case CoopOpKind.TileDelete:
                case CoopOpKind.DecorationOrder:
                    WriteIds(writer, Ids);
                    break;
                case CoopOpKind.TileSet:
                    writer.Write(Id);
                    writer.Write(Value);
                    break;
                case CoopOpKind.Upsert:
                    writer.Write(Id);
                    writer.Write(Anchor);
                    writer.Write(Decoration);
                    writer.Write(Created);
                    writer.Write(FloorTile);
                    writer.Write(Json);
                    break;
                case CoopOpKind.Remove:
                    writer.Write(Id);
                    writer.Write(Decoration);
                    break;
                case CoopOpKind.Settings:
                    writer.Write(SettingsType);
                    writer.Write(Json);
                    break;
            }
        }

        public static CoopOp Read(BinaryReader reader)
        {
            var op = new CoopOp { Kind = (CoopOpKind)reader.ReadByte() };
            switch (op.Kind)
            {
                case CoopOpKind.TileInsert:
                    op.Anchor = reader.ReadInt64();
                    op.Ids = ReadIds(reader);
                    op.Values = new List<string>(op.Ids.Count);
                    for (int i = 0; i < op.Ids.Count; i++) op.Values.Add(reader.ReadString());
                    break;
                case CoopOpKind.TileDelete:
                case CoopOpKind.DecorationOrder:
                    op.Ids = ReadIds(reader);
                    break;
                case CoopOpKind.TileSet:
                    op.Id = reader.ReadInt64();
                    op.Value = reader.ReadString();
                    break;
                case CoopOpKind.Upsert:
                    op.Id = reader.ReadInt64();
                    op.Anchor = reader.ReadInt64();
                    op.Decoration = reader.ReadBoolean();
                    op.Created = reader.ReadBoolean();
                    op.FloorTile = reader.ReadInt64();
                    op.Json = reader.ReadString();
                    break;
                case CoopOpKind.Remove:
                    op.Id = reader.ReadInt64();
                    op.Decoration = reader.ReadBoolean();
                    break;
                case CoopOpKind.Settings:
                    op.SettingsType = reader.ReadString();
                    op.Json = reader.ReadString();
                    break;
                default:
                    throw new InvalidDataException("未知的修改类型 " + op.Kind);
            }

            return op;
        }

        private static void WriteIds(BinaryWriter writer, List<long> ids)
        {
            writer.Write(ids.Count);
            foreach (long id in ids) writer.Write(id);
        }

        private static List<long> ReadIds(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var ids = new List<long>(count);
            for (int i = 0; i < count; i++) ids.Add(reader.ReadInt64());
            return ids;
        }
    }

    /// <summary>
    /// 一次编辑产生的一组修改。客人发给房主时 Seq = 0；房主排好顺序后带上全局序号 Seq 转发给所有人（包括作者）。
    /// 附带这些修改引用到的素材清单，收到的人缺文件就去要。
    /// </summary>
    internal sealed class CoopBatch
    {
        public ulong Author;
        public int AuthorSeq;
        public int Seq;
        public List<CoopOp> Ops = new List<CoopOp>();
        public List<ManifestEntry> Manifest = new List<ManifestEntry>();

        public byte[] Encode()
        {
            BinaryWriter writer = Packet.Writer(out MemoryStream stream);
            writer.Write(Author);
            writer.Write(AuthorSeq);
            writer.Write(Seq);
            writer.Write(Ops.Count);
            foreach (CoopOp op in Ops) op.Write(writer);
            writer.Write(Manifest.Count);
            foreach (ManifestEntry entry in Manifest) entry.Write(writer);
            return stream.ToArray();
        }

        public static CoopBatch Decode(byte[] payload)
        {
            BinaryReader reader = Packet.Reader(payload);
            var batch = new CoopBatch
            {
                Author = reader.ReadUInt64(),
                AuthorSeq = reader.ReadInt32(),
                Seq = reader.ReadInt32(),
            };
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++) batch.Ops.Add(CoopOp.Read(reader));
            int files = reader.ReadInt32();
            for (int i = 0; i < files; i++) batch.Manifest.Add(ManifestEntry.Read(reader));
            return batch;
        }
    }
}
