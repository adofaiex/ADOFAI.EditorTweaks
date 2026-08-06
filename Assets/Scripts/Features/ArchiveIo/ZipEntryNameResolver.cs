using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpSevenZip;

namespace EditorTweaks.Features.ArchiveIo
{
    internal sealed class ResolvedZipEntry
    {
        public ResolvedZipEntry(int index, string relativePath, bool isDirectory, ulong size)
        {
            Index = index;
            RelativePath = relativePath;
            IsDirectory = isDirectory;
            Size = size;
        }

        public int Index { get; }

        public string RelativePath { get; }

        public bool IsDirectory { get; }

        public ulong Size { get; }
    }

    internal sealed class ZipNameResolution
    {
        public ZipNameResolution(IReadOnlyList<ResolvedZipEntry> entries, string encodingName)
        {
            Entries = entries;
            EncodingName = encodingName;
        }

        public IReadOnlyList<ResolvedZipEntry> Entries { get; }

        public string EncodingName { get; }
    }

    internal static class ZipEntryNameResolver
    {
        private const int MaximumLevelTextBytes = 32 * 1024 * 1024;
        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public static ZipNameResolution ResolveZip(
            string archivePath,
            SharpSevenZipExtractor extractor,
            LegacyZipEncodingMode mode)
        {
            IReadOnlyList<RawZipEntry> rawEntries = ZipArchiveMetadata.Read(archivePath);
            IReadOnlyList<ArchiveFileInfo> archiveEntries = extractor.ArchiveFileData;
            if (rawEntries.Count != archiveEntries.Count)
            {
                throw new InvalidDataException(
                    $"ZIP entry table mismatch: metadata={rawEntries.Count}, 7-Zip={archiveEntries.Count}.");
            }

            List<int> legacyIndexes = rawEntries
                .Where(entry => entry.UnicodeName == null && !entry.UsesUtf8 && !IsAscii(entry.RawName))
                .Select(entry => entry.Index)
                .ToList();

            EncodingCandidate? legacyEncoding = null;
            if (legacyIndexes.Count > 0)
            {
                legacyEncoding = mode == LegacyZipEncodingMode.Auto
                    ? DetectEncoding(rawEntries, archiveEntries, extractor, legacyIndexes)
                    : CreateCandidate(mode);
            }

            List<ResolvedZipEntry> resolved = new List<ResolvedZipEntry>(rawEntries.Count);
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < rawEntries.Count; index++)
            {
                RawZipEntry rawEntry = rawEntries[index];
                ArchiveFileInfo archiveEntry = archiveEntries[index];
                string name = DecodeName(rawEntry, legacyEncoding);
                string relativePath = NormalizeAndValidatePath(name);
                if (!paths.Add(relativePath))
                {
                    throw new InvalidDataException(
                        "ZIP contains duplicate output path: " + relativePath);
                }

                bool isDirectory = archiveEntry.IsDirectory
                    || name.EndsWith("/", StringComparison.Ordinal)
                    || name.EndsWith("\\", StringComparison.Ordinal);
                resolved.Add(new ResolvedZipEntry(
                    archiveEntry.Index,
                    relativePath,
                    isDirectory,
                    archiveEntry.Size));
            }

            return new ZipNameResolution(
                resolved,
                legacyEncoding?.DisplayName ?? "UTF-8/Unicode");
        }

        public static ZipNameResolution ResolveNative(
            string archivePath,
            IReadOnlyList<ArchiveFileInfo> archiveEntries,
            string formatName)
        {
            List<ResolvedZipEntry> resolved =
                new List<ResolvedZipEntry>(archiveEntries.Count);
            HashSet<string> paths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (ArchiveFileInfo archiveEntry in archiveEntries)
            {
                string nativeName = archiveEntry.FileName;
                if (archiveEntry.IsDirectory
                    && IsArchiveRootEntry(nativeName))
                {
                    continue;
                }

                if (!archiveEntry.IsDirectory
                    && archiveEntries.Count == 1
                    && (string.IsNullOrWhiteSpace(nativeName)
                        || string.Equals(
                            nativeName,
                            "[no name]",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    nativeName = Path.GetFileNameWithoutExtension(archivePath);
                }

                string relativePath = NormalizeAndValidatePath(
                    nativeName);
                if (!paths.Add(relativePath))
                {
                    throw new InvalidDataException(
                        "Archive contains duplicate output path: " + relativePath);
                }

                resolved.Add(new ResolvedZipEntry(
                    archiveEntry.Index,
                    relativePath,
                    archiveEntry.IsDirectory,
                    archiveEntry.Size));
            }

            return new ZipNameResolution(
                resolved,
                "7-Zip/" + formatName);
        }

        private static bool IsArchiveRootEntry(string value)
        {
            string normalized = value.Replace('\\', '/').Trim('/');
            return normalized == ".";
        }

        private static EncodingCandidate DetectEncoding(
            IReadOnlyList<RawZipEntry> rawEntries,
            IReadOnlyList<ArchiveFileInfo> archiveEntries,
            SharpSevenZipExtractor extractor,
            IReadOnlyList<int> legacyIndexes)
        {
            HashSet<string> levelStrings = ReadLevelStrings(rawEntries, archiveEntries, extractor);
            List<EncodingCandidate> candidates = CreateAutoCandidates();
            EncodingCandidate? best = null;
            int bestScore = int.MinValue;
            foreach (EncodingCandidate candidate in candidates)
            {
                int score = ScoreCandidate(candidate, rawEntries, legacyIndexes, levelStrings);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best
                ?? throw new InvalidDataException(
                    "Unable to decode legacy ZIP filenames with the supported encodings.");
        }

        private static int ScoreCandidate(
            EncodingCandidate candidate,
            IReadOnlyList<RawZipEntry> entries,
            IReadOnlyList<int> legacyIndexes,
            HashSet<string> levelStrings)
        {
            int score = 0;
            foreach (int index in legacyIndexes)
            {
                string decoded;
                try
                {
                    decoded = candidate.Encoding.GetString(entries[index].RawName);
                    if (!ByteArraysEqual(
                        candidate.Encoding.GetBytes(decoded),
                        entries[index].RawName))
                    {
                        return int.MinValue;
                    }

                    decoded = NormalizeAndValidatePath(decoded);
                }
                catch (Exception exception) when (
                    exception is DecoderFallbackException
                    || exception is EncoderFallbackException
                    || exception is InvalidDataException
                    || exception is ArgumentException)
                {
                    return int.MinValue;
                }

                string normalized = NormalizeReference(decoded);
                string fileName = GetPortableFileName(normalized);
                if (levelStrings.Contains(normalized))
                {
                    score += 1000;
                }
                else if (levelStrings.Contains(fileName))
                {
                    score += 250;
                }

                score += ScoreScript(candidate.Mode, decoded);
                score -= CountSuspiciousCharacters(decoded) * 8;
            }

            return score;
        }

        private static HashSet<string> ReadLevelStrings(
            IReadOnlyList<RawZipEntry> rawEntries,
            IReadOnlyList<ArchiveFileInfo> archiveEntries,
            SharpSevenZipExtractor extractor)
        {
            HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < rawEntries.Count; index++)
            {
                if (archiveEntries[index].IsDirectory
                    || archiveEntries[index].Size > MaximumLevelTextBytes
                    || !EndsWithAscii(rawEntries[index].RawName, ".adofai"))
                {
                    continue;
                }

                try
                {
                    using MemoryStream stream = new MemoryStream(
                        (int)Math.Min(archiveEntries[index].Size, int.MaxValue));
                    extractor.ExtractFile(archiveEntries[index].Index, stream);
                    stream.Position = 0;
                    using StreamReader reader = new StreamReader(
                        stream,
                        new UTF8Encoding(false, true),
                        detectEncodingFromByteOrderMarks: true,
                        bufferSize: 4096,
                        leaveOpen: true);
                    object root = GDMiniJSON.Json.Deserialize(reader.ReadToEnd());
                    CollectStringValues(root, values);
                }
                catch (Exception exception)
                {
                    Main.Log(
                        "[ArchiveIo] Unable to inspect level text for filename detection: "
                        + exception.GetBaseException().Message);
                }
            }

            return values;
        }

        private static void CollectStringValues(object? value, HashSet<string> values)
        {
            if (value is string text)
            {
                string normalized = NormalizeReference(text);
                if (!string.IsNullOrEmpty(normalized))
                {
                    values.Add(normalized);
                    values.Add(GetPortableFileName(normalized));
                }

                return;
            }

            if (value is Dictionary<string, object> dictionary)
            {
                foreach (object child in dictionary.Values)
                {
                    CollectStringValues(child, values);
                }

                return;
            }

            if (value is List<object> list)
            {
                foreach (object child in list)
                {
                    CollectStringValues(child, values);
                }
            }
        }

        private static string DecodeName(
            RawZipEntry entry,
            EncodingCandidate? legacyEncoding)
        {
            if (entry.UsesUtf8)
            {
                try
                {
                    return StrictUtf8.GetString(entry.RawName);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new InvalidDataException(
                        "ZIP entry is marked UTF-8 but contains an invalid filename.",
                        exception);
                }
            }

            if (entry.UnicodeName != null)
            {
                return entry.UnicodeName;
            }

            if (IsAscii(entry.RawName))
            {
                return Encoding.ASCII.GetString(entry.RawName);
            }

            if (legacyEncoding == null)
            {
                throw new InvalidDataException("Legacy ZIP filename encoding was not resolved.");
            }

            try
            {
                string decoded = legacyEncoding.Encoding.GetString(entry.RawName);
                if (!ByteArraysEqual(
                    legacyEncoding.Encoding.GetBytes(decoded),
                    entry.RawName))
                {
                    throw new InvalidDataException(
                        "Legacy ZIP filename cannot be round-tripped with "
                        + legacyEncoding.DisplayName
                        + ".");
                }

                return decoded;
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "Legacy ZIP filename is invalid for " + legacyEncoding.DisplayName + ".",
                    exception);
            }
            catch (EncoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "Legacy ZIP filename is invalid for " + legacyEncoding.DisplayName + ".",
                    exception);
            }
        }

        private static List<EncodingCandidate> CreateAutoCandidates()
        {
            List<EncodingCandidate> candidates = new List<EncodingCandidate>();
            AddCandidate(candidates, Encoding.Default.CodePage, LegacyZipEncodingMode.Auto, "System CP" + Encoding.Default.CodePage);
            AddCandidate(candidates, 949, LegacyZipEncodingMode.CP949, "CP949");
            AddCandidate(candidates, 54936, LegacyZipEncodingMode.GB18030, "GB18030");
            AddCandidate(candidates, 932, LegacyZipEncodingMode.ShiftJIS, "Shift-JIS");
            AddCandidate(candidates, 437, LegacyZipEncodingMode.CP437, "CP437");
            return candidates;
        }

        private static EncodingCandidate CreateCandidate(LegacyZipEncodingMode mode)
        {
            switch (mode)
            {
                case LegacyZipEncodingMode.CP949:
                    return CreateCandidate(949, mode, "CP949");
                case LegacyZipEncodingMode.GB18030:
                    return CreateCandidate(54936, mode, "GB18030");
                case LegacyZipEncodingMode.ShiftJIS:
                    return CreateCandidate(932, mode, "Shift-JIS");
                case LegacyZipEncodingMode.CP437:
                    return CreateCandidate(437, mode, "CP437");
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static void AddCandidate(
            ICollection<EncodingCandidate> candidates,
            int codePage,
            LegacyZipEncodingMode mode,
            string displayName)
        {
            if (candidates.Any(item => item.Encoding.CodePage == codePage))
            {
                return;
            }

            candidates.Add(CreateCandidate(codePage, mode, displayName));
        }

        private static EncodingCandidate CreateCandidate(
            int codePage,
            LegacyZipEncodingMode mode,
            string displayName)
        {
            Encoding encoding = Encoding.GetEncoding(
                codePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            return new EncodingCandidate(mode, encoding, displayName);
        }

        private static int ScoreScript(LegacyZipEncodingMode mode, string value)
        {
            int score = 0;
            foreach (char character in value)
            {
                if (mode == LegacyZipEncodingMode.CP949
                    && character >= '\uAC00'
                    && character <= '\uD7AF')
                {
                    score += 5;
                }
                else if (mode == LegacyZipEncodingMode.GB18030
                    && character >= '\u4E00'
                    && character <= '\u9FFF')
                {
                    score += 2;
                }
                else if (mode == LegacyZipEncodingMode.ShiftJIS
                    && ((character >= '\u3040' && character <= '\u30FF')
                        || (character >= '\uFF66' && character <= '\uFF9D')))
                {
                    score += 5;
                }
            }

            return score;
        }

        private static int CountSuspiciousCharacters(string value)
        {
            int count = 0;
            foreach (char character in value)
            {
                if ((character >= '\u0080' && character <= '\u009F')
                    || (character >= '\u2500' && character <= '\u259F')
                    || (character >= '\uE000' && character <= '\uF8FF'))
                {
                    count++;
                }
            }

            return count;
        }

        private static string NormalizeAndValidatePath(string value)
        {
            string normalized = value.Replace('\\', '/').TrimEnd('/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(normalized)
                || normalized.IndexOf('\0') >= 0)
            {
                throw new InvalidDataException("ZIP entry path is invalid: " + value);
            }

            string[] segments = normalized.Split('/');
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment)
                    || segment == "."
                    || segment == ".."
                    || segment.IndexOfAny(invalidCharacters) >= 0
                    || segment.Any(char.IsControl))
                {
                    throw new InvalidDataException("ZIP entry path is invalid: " + value);
                }
            }

            return string.Join("/", segments);
        }

        private static string NormalizeReference(string value)
        {
            return value.Replace('\\', '/').TrimStart('.', '/');
        }

        private static string GetPortableFileName(string value)
        {
            int separator = value.LastIndexOf('/');
            return separator >= 0 ? value.Substring(separator + 1) : value;
        }

        private static bool IsAscii(byte[] data)
        {
            return data.All(value => value < 0x80);
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EndsWithAscii(byte[] data, string suffix)
        {
            if (data.Length < suffix.Length)
            {
                return false;
            }

            int offset = data.Length - suffix.Length;
            for (int index = 0; index < suffix.Length; index++)
            {
                byte value = data[offset + index];
                char expected = suffix[index];
                if (value >= 'A' && value <= 'Z')
                {
                    value = (byte)(value + ('a' - 'A'));
                }

                if (value != expected)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class EncodingCandidate
        {
            public EncodingCandidate(
                LegacyZipEncodingMode mode,
                Encoding encoding,
                string displayName)
            {
                Mode = mode;
                Encoding = encoding;
                DisplayName = displayName;
            }

            public LegacyZipEncodingMode Mode { get; }

            public Encoding Encoding { get; }

            public string DisplayName { get; }
        }
    }
}
