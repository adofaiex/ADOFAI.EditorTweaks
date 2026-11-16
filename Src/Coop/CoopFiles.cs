#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ADOFAI;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>后台线程把活儿做完后，回到主线程（Unity 线程）执行的回调队列。每帧由 CoopSession 清空。</summary>
    internal static class MainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();

        public static void Post(Action action)
        {
            Queue.Enqueue(action);
        }

        public static void Drain()
        {
            while (Queue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Main.Log("[Coop] " + exception);
                }
            }
        }
    }

    /// <summary>
    /// 文件内容哈希（sha256）缓存：按路径记住大小、修改时间和哈希，没变过的文件不重新算。
    /// 只在主线程读写；真正算哈希放到后台线程。
    /// </summary>
    internal static class FileHashes
    {
        private static readonly Dictionary<string, Entry> ByPath = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ByHash = new Dictionary<string, string>();

        /// <summary>算一组文件的哈希（缓存命中的直接用），完成后在主线程回调：路径 → 哈希（文件不存在为 null）。</summary>
        public static void HashAsync(IReadOnlyCollection<string> paths, Action<Dictionary<string, string>> done)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var todo = new List<KeyValuePair<string, Stamp>>();
            foreach (string path in paths)
            {
                if (result.ContainsKey(path)) continue;
                Stamp stamp = Stamp.Of(path);
                if (!stamp.Exists)
                {
                    result[path] = null;
                    continue;
                }

                if (ByPath.TryGetValue(path, out Entry entry) && entry.Stamp.Equals(stamp))
                {
                    result[path] = entry.Sha;
                    continue;
                }

                todo.Add(new KeyValuePair<string, Stamp>(path, stamp));
            }

            if (todo.Count == 0)
            {
                done(result);
                return;
            }

            Task.Run(() =>
            {
                var computed = new List<KeyValuePair<string, Entry>>();
                foreach (KeyValuePair<string, Stamp> item in todo)
                {
                    string sha = null;
                    try
                    {
                        sha = HashFile(item.Key);
                    }
                    catch (Exception exception)
                    {
                        Main.Log("[Coop] Hashing " + item.Key + " failed: " + exception.Message);
                    }

                    computed.Add(new KeyValuePair<string, Entry>(item.Key, new Entry(item.Value, sha)));
                }

                MainThread.Post(() =>
                {
                    foreach (KeyValuePair<string, Entry> item in computed)
                    {
                        result[item.Key] = item.Value.Sha;
                        if (item.Value.Sha != null) Remember(item.Key, item.Value.Sha, item.Value.Stamp);
                    }

                    done(result);
                });
            });
        }

        /// <summary>记下一个刚写好的文件的哈希（收到文件后用，免得再算一遍）。</summary>
        public static void Remember(string path, string sha)
        {
            Remember(path, sha, Stamp.Of(path));
        }

        /// <summary>本机哪个文件是这个哈希（文件改过或删了就当没有）。</summary>
        public static string FindPath(string sha)
        {
            if (!ByHash.TryGetValue(sha, out string path)) return null;
            if (ByPath.TryGetValue(path, out Entry entry) && entry.Sha == sha && entry.Stamp.Equals(Stamp.Of(path))) return path;
            ByHash.Remove(sha);
            return null;
        }

        public static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        public static string ToHex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 15];
            }

            return new string(chars);
        }

        private static void Remember(string path, string sha, Stamp stamp)
        {
            ByPath[path] = new Entry(stamp, sha);
            ByHash[sha] = path;
        }

        private readonly struct Entry
        {
            public Entry(Stamp stamp, string sha)
            {
                Stamp = stamp;
                Sha = sha;
            }

            public Stamp Stamp { get; }
            public string Sha { get; }
        }

        private readonly struct Stamp : IEquatable<Stamp>
        {
            private Stamp(bool exists, long size, long ticks)
            {
                Exists = exists;
                Size = size;
                Ticks = ticks;
            }

            public bool Exists { get; }
            public long Size { get; }
            public long Ticks { get; }

            public static Stamp Of(string path)
            {
                try
                {
                    var info = new FileInfo(path);
                    return info.Exists ? new Stamp(true, info.Length, info.LastWriteTimeUtc.Ticks) : default;
                }
                catch (Exception)
                {
                    return default;
                }
            }

            public bool Equals(Stamp other)
            {
                return Exists == other.Exists && Size == other.Size && Ticks == other.Ticks;
            }
        }
    }

    /// <summary>素材清单的一项：谱面文件夹里的相对路径、内容哈希、大小。</summary>
    internal sealed class ManifestEntry
    {
        public ManifestEntry(string name, string sha, long size)
        {
            Name = name;
            Sha = sha;
            Size = size;
        }

        public string Name { get; }
        public string Sha { get; }
        public long Size { get; }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Name);
            writer.Write(Sha);
            writer.Write(Size);
        }

        public static ManifestEntry Read(BinaryReader reader)
        {
            return new ManifestEntry(reader.ReadString(), reader.ReadString(), reader.ReadInt64());
        }
    }

    /// <summary>谱面引用的外部文件：收集、路径安全检查、覆盖前备份。</summary>
    internal static class LevelFiles
    {
        /// <summary>
        /// 谱面里引用了哪些（存在于谱面文件夹里的）文件：把所有事件、装饰、设置里的字符串值拿来，
        /// 看谱面文件夹里有没有这个文件。这样歌曲、背景、装饰图片、视频、轨道贴图、自定义音效都能覆盖到。
        /// </summary>
        public static List<string> CollectReferenced(LevelData data, string levelDir)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var checkedValues = new HashSet<string>(StringComparer.Ordinal);

            void Scan(LevelEvent levelEvent)
            {
                if (levelEvent == null) return;
                foreach (KeyValuePair<string, object> pair in levelEvent.GetData())
                {
                    if (!(pair.Value is string value) || value.Length < 3 || value.Length > 240) continue;
                    if (!checkedValues.Add(value)) continue;
                    if (value.IndexOf('.') < 0 || !IsSafeRelative(value)) continue;
                    try
                    {
                        if (File.Exists(Path.Combine(levelDir, value))) names.Add(value);
                    }
                    catch (Exception)
                    {
                        // 文件名里有非法字符之类，忽略
                    }
                }
            }

            foreach (LevelEvent settings in data.settings) Scan(settings);
            foreach (LevelEvent levelEvent in data.levelEvents) Scan(levelEvent);
            foreach (LevelEvent decoration in data.decorations) Scan(decoration);

            var list = new List<string>(names);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>网络上传来的文件名只允许是谱面文件夹里面的相对路径，不能跳出去。</summary>
        public static bool IsSafeRelative(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 240) return false;
            if (name.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || name.IndexOf(':') >= 0) return false;
            if (Path.IsPathRooted(name) || name.StartsWith("/") || name.StartsWith("\\")) return false;
            foreach (string part in name.Split('/', '\\'))
                if (part == ".." || part == ".") return false;
            return true;
        }

        /// <summary>覆盖前把原文件复制到 &lt;谱面文件夹&gt;/.coop-backup/&lt;时间&gt;/ 下（保持相对路径）。</summary>
        public static void Backup(string levelDir, string relativeName, string stamp)
        {
            string source = Path.Combine(levelDir, relativeName);
            if (!File.Exists(source)) return;
            string target = Path.Combine(levelDir, ".coop-backup", stamp, relativeName);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (!File.Exists(target)) File.Copy(source, target);
        }

        public static string NewBackupStamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }

        /// <summary>Mod 自己的数据目录（游戏的 persistentDataPath 下）。</summary>
        public static string DataDir(params string[] parts)
        {
            string path = Path.Combine(Persistence.DataPath, "EditorTweaks");
            foreach (string part in parts) path = Path.Combine(path, part);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>正在接收的一个文件：边收边写临时文件、边算哈希，收完校验后复制到目标位置。</summary>
    internal sealed class IncomingFile
    {
        private readonly string _tempPath;
        private FileStream _stream;
        private SHA256 _hash;

        public IncomingFile(string sha, long size)
        {
            Sha = sha;
            Size = size;
            _tempPath = Path.Combine(LevelFiles.DataDir("CoopTemp"), sha + ".part");
        }

        public string Sha { get; }

        public long Size { get; }

        public long Received { get; private set; }

        /// <summary>收完后要放到哪些地方（完整路径）。</summary>
        public HashSet<string> Targets { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>从谁那里下载。</summary>
        public CoopPeer Source { get; set; }

        /// <summary>备份用：目标所在的谱面文件夹和相对路径。</summary>
        public string LevelDir { get; set; }

        /// <summary>收到一块。返回 false 表示出错（偏移对不上、写盘失败）。</summary>
        public bool Write(long offset, byte[] data)
        {
            try
            {
                if (offset != Received) return false;
                if (_stream == null)
                {
                    _stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    _hash = SHA256.Create();
                }

                _stream.Write(data, 0, data.Length);
                _hash.TransformBlock(data, 0, data.Length, null, 0);
                Received += data.Length;
                return true;
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] Writing " + _tempPath + " failed: " + exception.Message);
                return false;
            }
        }

        public bool Complete => Received >= Size;

        /// <summary>收完了：校验哈希，备份目标处的旧文件，再复制过去。返回成功放好的路径。</summary>
        public List<string> Finish(string backupStamp, out string error)
        {
            error = null;
            var placed = new List<string>();
            try
            {
                if (_stream == null)
                {
                    // 空文件
                    _stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    _hash = SHA256.Create();
                }

                _stream.Dispose();
                _stream = null;
                _hash.TransformFinalBlock(new byte[0], 0, 0);
                string actual = FileHashes.ToHex(_hash.Hash);
                _hash.Dispose();
                _hash = null;
                if (actual != Sha)
                {
                    error = "校验失败";
                    return placed;
                }

                foreach (string target in Targets)
                {
                    if (File.Exists(target))
                    {
                        if (LevelDir != null && target.StartsWith(LevelDir, StringComparison.OrdinalIgnoreCase))
                            LevelFiles.Backup(LevelDir, target.Substring(LevelDir.Length).TrimStart('/', '\\'), backupStamp);
                        File.Delete(target);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(_tempPath, target);
                    FileHashes.Remember(target, Sha);
                    placed.Add(target);
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }
            finally
            {
                Abort();
            }

            return placed;
        }

        public void Abort()
        {
            _stream?.Dispose();
            _stream = null;
            _hash?.Dispose();
            _hash = null;
            try
            {
                if (File.Exists(_tempPath)) File.Delete(_tempPath);
            }
            catch (Exception)
            {
                // 删不掉就算了，下次覆盖
            }
        }
    }
}
