#nullable disable
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 一份完整的谱面状态（客人加入、或者校验发现不一致时由房主发）：对应的修改序号、谱面文件名、谱面身份、
    /// 素材清单、谱面 JSON（gzip 压缩传输），以及 ID 表（客人打开谱面后按它给砖块、事件、装饰编上和房主一样的 ID）。
    /// </summary>
    internal sealed class CoopSnapshot
    {
        public int Seq;
        public byte[] IdTable = new byte[0];
        public string LevelFileName;
        public string Identity;
        public string SongName;
        public List<ManifestEntry> Manifest = new List<ManifestEntry>();
        public string Json;

        /// <summary>收到的原始消息内容（房主原样转发用）。</summary>
        public byte[] Payload;

        public byte[] Encode()
        {
            BinaryWriter writer = Packet.Writer(out MemoryStream stream);
            writer.Write(Seq);
            writer.Write(IdTable.Length);
            writer.Write(IdTable);
            writer.Write(LevelFileName ?? string.Empty);
            writer.Write(Identity ?? string.Empty);
            writer.Write(SongName ?? string.Empty);
            writer.Write(Manifest.Count);
            foreach (ManifestEntry entry in Manifest) entry.Write(writer);
            byte[] compressed = Compress(Json);
            writer.Write(compressed.Length);
            writer.Write(compressed);
            Payload = stream.ToArray();
            return Payload;
        }

        public static CoopSnapshot Decode(byte[] payload)
        {
            BinaryReader reader = Packet.Reader(payload);
            var snapshot = new CoopSnapshot
            {
                Payload = payload,
                Seq = reader.ReadInt32(),
                IdTable = reader.ReadBytes(reader.ReadInt32()),
                LevelFileName = reader.ReadString(),
                Identity = reader.ReadString(),
                SongName = reader.ReadString(),
            };
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++) snapshot.Manifest.Add(ManifestEntry.Read(reader));
            int length = reader.ReadInt32();
            snapshot.Json = Decompress(reader.ReadBytes(length));
            return snapshot;
        }

        private static byte[] Compress(string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text ?? string.Empty);
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    gzip.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        private static string Decompress(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
