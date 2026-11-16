#nullable disable
using System.IO;
using System.Text;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>多人制谱的网络协议常量。改了消息格式就把 Version 加一，不同版本的人进不了同一个房间。</summary>
    internal static class CoopProtocol
    {
        public const int Version = 1;

        /// <summary>Steam 中继 socket 的虚拟端口（同一个 Steam 账号上区分不同用途的连接）。</summary>
        public const int VirtualPort = 7713;

        /// <summary>房间数据里标记"这是 EditorTweaks 多人制谱房间"的键，值是协议版本。</summary>
        public const string LobbyKey = "etw_coop";

        public const string LobbyLevelKey = "etw_level";

        public const int MaxMembers = 8;

        /// <summary>单条 Steam 消息最大 512KB，这里分片留足余量。</summary>
        public const int FragmentSize = 256 * 1024;

        /// <summary>文件按这么大一块发。</summary>
        public const int FileChunkSize = 128 * 1024;

        /// <summary>Steam 发送缓冲里积压的可靠数据超过这个数，就先不往里塞文件块（让编辑数据插得进去）。</summary>
        public const int FilePendingBudget = 1024 * 1024;
    }

    /// <summary>通道：编辑数据和文件分开走，大文件不会堵住编辑。</summary>
    internal enum CoopLane : ushort
    {
        Control = 0,
        Files = 1,
    }

    internal enum CoopMessage : byte
    {
        /// <summary>客人 → 房主：协议版本、名字。</summary>
        Hello = 1,

        /// <summary>房主 → 客人：欢迎，附带房主名字。</summary>
        Welcome = 2,

        /// <summary>房主 → 客人：拒绝（原因）。</summary>
        Reject = 3,

        /// <summary>整份谱面（作者、作者序号、谱面文件名、素材清单、压缩的 JSON）。</summary>
        Snapshot = 10,

        /// <summary>请求文件（一组 sha256）。</summary>
        FileRequest = 20,

        /// <summary>文件的一块（sha、总大小、偏移、数据）。</summary>
        FileData = 21,

        /// <summary>对方没有这个文件。</summary>
        FileMissing = 22,
    }

    /// <summary>消息体读写的小工具（BinaryWriter / BinaryReader，字符串用 UTF-8）。</summary>
    internal static class Packet
    {
        public static BinaryWriter Writer(out MemoryStream stream)
        {
            stream = new MemoryStream();
            return new BinaryWriter(stream, Encoding.UTF8);
        }

        public static BinaryReader Reader(byte[] payload)
        {
            return new BinaryReader(new MemoryStream(payload, false), Encoding.UTF8);
        }
    }
}
