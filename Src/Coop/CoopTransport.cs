#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>
    /// 一条连接（房主那边每个客人一条，客人那边只有通往房主的一条）。
    /// 负责：大消息分片、按通道排队发送（Steam 缓冲满了就下一帧再发）、收到的分片拼回整条消息、文件流式发送。
    /// 帧格式：[通道][种类][...]，种类 0=整条 [类型][内容]，1=首片 [类型][总长][数据]，2=中间片，3=末片。
    /// </summary>
    internal sealed class CoopPeer
    {
        private const byte Whole = 0;
        private const byte First = 1;
        private const byte Middle = 2;
        private const byte Last = 3;

        private readonly Queue<byte[]>[] _outgoing = { new Queue<byte[]>(), new Queue<byte[]>() };
        private readonly Reassembly[] _incoming = { new Reassembly(), new Reassembly() };
        private readonly Queue<FileJob> _fileJobs = new Queue<FileJob>();
        private FileJob _activeJob;
        private bool _lanesConfigured;

        public CoopPeer(Connection connection, ulong steamId, string name)
        {
            Connection = connection;
            SteamId = steamId;
            Name = name;
        }

        public Connection Connection { get; }

        public ulong SteamId { get; set; }

        public string Name { get; set; }

        /// <summary>房主那边：握手通过了（收到了版本一致的 Hello）。</summary>
        public bool Accepted { get; set; }

        /// <summary>正在发给这个人的文件剩多少字节（进度显示用）。</summary>
        public long UploadRemaining
        {
            get
            {
                long total = 0;
                if (_activeJob != null) total += _activeJob.Size - _activeJob.Offset;
                foreach (FileJob job in _fileJobs) total += job.Size;
                return total;
            }
        }

        // ---------------------------------------------------------------- 发送

        public void Send(CoopLane lane, CoopMessage type, byte[] payload)
        {
            Queue<byte[]> queue = _outgoing[(int)lane];
            if (payload.Length + 3 <= CoopProtocol.FragmentSize)
            {
                var frame = new byte[payload.Length + 3];
                frame[0] = (byte)lane;
                frame[1] = Whole;
                frame[2] = (byte)type;
                Buffer.BlockCopy(payload, 0, frame, 3, payload.Length);
                queue.Enqueue(frame);
                return;
            }

            int offset = 0;
            bool first = true;
            while (offset < payload.Length)
            {
                int header = first ? 7 : 2;
                int count = Math.Min(CoopProtocol.FragmentSize - header, payload.Length - offset);
                var frame = new byte[count + header];
                frame[0] = (byte)lane;
                frame[1] = first ? First : offset + count >= payload.Length ? Last : Middle;
                if (first)
                {
                    frame[2] = (byte)type;
                    WriteInt(frame, 3, payload.Length);
                }

                Buffer.BlockCopy(payload, offset, frame, header, count);
                queue.Enqueue(frame);
                offset += count;
                first = false;
            }
        }

        /// <summary>排队把一个文件发给对方（在文件通道上一块块地发）。</summary>
        public void QueueFile(string sha, string path)
        {
            foreach (FileJob job in _fileJobs)
                if (job.Sha == sha) return;
            if (_activeJob != null && _activeJob.Sha == sha) return;
            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (Exception)
            {
                SendFileMissing(sha);
                return;
            }

            _fileJobs.Enqueue(new FileJob(sha, path, size));
        }

        public void SendFileMissing(string sha)
        {
            BinaryWriter writer = Packet.Writer(out MemoryStream stream);
            writer.Write(sha);
            Send(CoopLane.Control, CoopMessage.FileMissing, stream.ToArray());
        }

        /// <summary>每帧调用：把排队的帧交给 Steam；Steam 缓冲不太满时再切下一块文件。</summary>
        public void Pump()
        {
            if (!_lanesConfigured)
            {
                // 通道 0（编辑数据）优先级高于通道 1（文件）：数字越小越优先。连上之前配置会失败，下一帧再试
                if (Connection.ConfigureConnectionLanes(new[] { 0, 1 }, new ushort[] { 1, 1 }) != Result.OK) return;
                _lanesConfigured = true;
            }

            if (!Flush(_outgoing[(int)CoopLane.Control])) return;
            if (!Flush(_outgoing[(int)CoopLane.Files])) return;

            int pending = Connection.QuickStatus().PendingReliable;
            while (pending < CoopProtocol.FilePendingBudget)
            {
                if (_activeJob == null)
                {
                    if (_fileJobs.Count == 0) return;
                    _activeJob = _fileJobs.Dequeue();
                }

                byte[] chunk = _activeJob.NextChunk(out bool failed);
                if (failed)
                {
                    SendFileMissing(_activeJob.Sha);
                    _activeJob.Dispose();
                    _activeJob = null;
                    continue;
                }

                Send(CoopLane.Files, CoopMessage.FileData, chunk);
                pending += chunk.Length;
                if (_activeJob.Done)
                {
                    _activeJob.Dispose();
                    _activeJob = null;
                }

                if (!Flush(_outgoing[(int)CoopLane.Files])) return;
            }
        }

        private bool Flush(Queue<byte[]> queue)
        {
            while (queue.Count > 0)
            {
                Result result = Connection.SendMessage(queue.Peek(), SendType.Reliable, (ushort)queue.Peek()[0]);
                if (result == Result.OK)
                {
                    queue.Dequeue();
                    continue;
                }

                if (result == Result.LimitExceeded) return false;
                // 连接已经断了之类：丢掉，断线回调会处理
                Main.Log("[Coop] SendMessage failed: " + result);
                queue.Clear();
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            _activeJob?.Dispose();
            _activeJob = null;
            _fileJobs.Clear();
            foreach (Queue<byte[]> queue in _outgoing) queue.Clear();
        }

        // ---------------------------------------------------------------- 接收

        /// <summary>收到一帧；拼成整条消息后返回 true。</summary>
        public bool Receive(IntPtr data, int size, out CoopMessage type, out byte[] payload)
        {
            type = 0;
            payload = null;
            if (size < 2) return false;
            var frame = new byte[size];
            Marshal.Copy(data, frame, 0, size);
            int lane = frame[0];
            if (lane >= _incoming.Length) return false;
            Reassembly assembly = _incoming[lane];

            switch (frame[1])
            {
                case Whole:
                    if (size < 3) return false;
                    type = (CoopMessage)frame[2];
                    payload = new byte[size - 3];
                    Buffer.BlockCopy(frame, 3, payload, 0, payload.Length);
                    return true;
                case First:
                    if (size < 7) return false;
                    assembly.Type = (CoopMessage)frame[2];
                    assembly.Buffer = new byte[ReadInt(frame, 3)];
                    assembly.Offset = 0;
                    assembly.Append(frame, 7);
                    return false;
                case Middle:
                    assembly.Append(frame, 2);
                    return false;
                case Last:
                    if (assembly.Buffer == null) return false;
                    assembly.Append(frame, 2);
                    type = assembly.Type;
                    payload = assembly.Buffer;
                    assembly.Buffer = null;
                    return assembly.Offset == payload.Length;
                default:
                    return false;
            }
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt(byte[] buffer, int offset)
        {
            return buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24;
        }

        private sealed class Reassembly
        {
            public CoopMessage Type;
            public byte[] Buffer;
            public int Offset;

            public void Append(byte[] frame, int start)
            {
                if (Buffer == null) return;
                int count = Math.Min(frame.Length - start, Buffer.Length - Offset);
                System.Buffer.BlockCopy(frame, start, Buffer, Offset, count);
                Offset += count;
            }
        }

        /// <summary>正在发送的一个文件：按块读，每块打成一条 FileData 消息。</summary>
        private sealed class FileJob
        {
            private readonly string _path;
            private FileStream _stream;

            public FileJob(string sha, string path, long size)
            {
                Sha = sha;
                _path = path;
                Size = size;
            }

            public string Sha { get; }

            public long Size { get; }

            public long Offset { get; private set; }

            public bool Done => Offset >= Size;

            public byte[] NextChunk(out bool failed)
            {
                failed = false;
                try
                {
                    if (_stream == null) _stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var buffer = new byte[(int)Math.Min(CoopProtocol.FileChunkSize, Size - Offset)];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = _stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }

                    if (read != buffer.Length)
                    {
                        failed = true;
                        return null;
                    }

                    BinaryWriter writer = Packet.Writer(out MemoryStream stream);
                    writer.Write(Sha);
                    writer.Write(Size);
                    writer.Write(Offset);
                    writer.Write(buffer.Length);
                    writer.Write(buffer);
                    Offset += buffer.Length;
                    return stream.ToArray();
                }
                catch (Exception exception)
                {
                    Main.Log("[Coop] Reading " + _path + " failed: " + exception.Message);
                    failed = true;
                    return null;
                }
            }

            public void Dispose()
            {
                _stream?.Dispose();
                _stream = null;
            }
        }
    }

    /// <summary>房主的监听 socket（Steam 中继）。事件转给 <see cref="CoopSession"/>。</summary>
    internal sealed class CoopHostSocket : SocketManager
    {
        public override void OnConnecting(Connection connection, ConnectionInfo info)
        {
            // 只接受已经在 Steam 房间里的人（防止知道房主 SteamID 的陌生人直接连进来）
            if (!CoopSession.IsLobbyMember(info.Identity.SteamId))
            {
                connection.Close(false, 0, "not in lobby");
                return;
            }

            base.OnConnecting(connection, info);
        }

        public override void OnConnected(Connection connection, ConnectionInfo info)
        {
            base.OnConnected(connection, info);
            CoopSession.HostPeerConnected(connection, info.Identity.SteamId);
        }

        public override void OnDisconnected(Connection connection, ConnectionInfo info)
        {
            base.OnDisconnected(connection, info);
            CoopSession.HostPeerDisconnected(connection);
        }

        public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            CoopSession.HostReceived(connection, data, size);
        }
    }

    /// <summary>客人连到房主的连接（Steam 中继）。</summary>
    internal sealed class CoopClientConnection : ConnectionManager
    {
        public override void OnConnected(ConnectionInfo info)
        {
            base.OnConnected(info);
            CoopSession.ClientConnected();
        }

        public override void OnDisconnected(ConnectionInfo info)
        {
            base.OnDisconnected(info);
            CoopSession.ClientDisconnected(info.EndReason.ToString());
        }

        public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            CoopSession.ClientReceived(data, size);
        }
    }
}
