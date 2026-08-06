using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EditorTweaks.Features.ArchiveIo
{
    internal sealed class RawZipEntry
    {
        public RawZipEntry(int index, ushort flags, byte[] rawName, byte[] extra)
        {
            Index = index;
            Flags = flags;
            RawName = rawName;
            UnicodeName = TryReadUnicodePath(rawName, extra);
        }

        public int Index { get; }

        public ushort Flags { get; }

        public byte[] RawName { get; }

        public string? UnicodeName { get; }

        public bool UsesUtf8 => (Flags & 0x0800) != 0;

        private static string? TryReadUnicodePath(byte[] rawName, byte[] extra)
        {
            int offset = 0;
            while (offset + 4 <= extra.Length)
            {
                ushort id = ReadUInt16(extra, offset);
                ushort size = ReadUInt16(extra, offset + 2);
                offset += 4;
                if (offset + size > extra.Length)
                {
                    return null;
                }

                if (id == 0x7075 && size >= 5 && extra[offset] == 1)
                {
                    uint expectedCrc = ReadUInt32(extra, offset + 1);
                    if (expectedCrc == Crc32.Compute(rawName))
                    {
                        try
                        {
                            return StrictUtf8.GetString(extra, offset + 5, size - 5);
                        }
                        catch (DecoderFallbackException)
                        {
                            return null;
                        }
                    }
                }

                offset += size;
            }

            return null;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }

        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }

    internal static class ZipArchiveMetadata
    {
        private const uint EndOfCentralDirectorySignature = 0x06054B50;
        private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
        private const uint Zip64LocatorSignature = 0x07064B50;
        private const uint CentralDirectoryEntrySignature = 0x02014B50;
        private const int MaximumCommentLength = ushort.MaxValue;
        private const int MaximumEntryCount = 10000;

        public static IReadOnlyList<RawZipEntry> Read(string archivePath)
        {
            using FileStream stream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            long eocdOffset = FindEndOfCentralDirectory(stream);
            stream.Position = eocdOffset + 4;
            ushort diskNumber = reader.ReadUInt16();
            ushort centralDirectoryDisk = reader.ReadUInt16();
            ushort entriesOnDisk = reader.ReadUInt16();
            ushort totalEntries16 = reader.ReadUInt16();
            uint centralSize32 = reader.ReadUInt32();
            uint centralOffset32 = reader.ReadUInt32();

            if (diskNumber != 0 || centralDirectoryDisk != 0 || entriesOnDisk != totalEntries16)
            {
                throw new InvalidDataException("Multi-volume ZIP archives are not supported.");
            }

            ulong totalEntries = totalEntries16;
            ulong centralOffset = centralOffset32;
            if (totalEntries16 == ushort.MaxValue
                || centralSize32 == uint.MaxValue
                || centralOffset32 == uint.MaxValue)
            {
                ReadZip64DirectoryInfo(
                    stream,
                    reader,
                    eocdOffset,
                    out totalEntries,
                    out centralOffset);
            }

            if (totalEntries > MaximumEntryCount)
            {
                throw new IOException(
                    $"ZIP extraction aborted: too many entries ({totalEntries} > {MaximumEntryCount}).");
            }

            if (centralOffset > (ulong)stream.Length)
            {
                throw new InvalidDataException("ZIP central directory metadata is invalid.");
            }

            stream.Position = (long)centralOffset;
            List<RawZipEntry> entries = new List<RawZipEntry>((int)totalEntries);
            for (int index = 0; index < (int)totalEntries; index++)
            {
                if (reader.ReadUInt32() != CentralDirectoryEntrySignature)
                {
                    throw new InvalidDataException("ZIP central directory entry is invalid.");
                }

                reader.ReadUInt16();
                reader.ReadUInt16();
                ushort flags = reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadUInt32();
                reader.ReadUInt32();
                reader.ReadUInt32();
                ushort nameLength = reader.ReadUInt16();
                ushort extraLength = reader.ReadUInt16();
                ushort commentLength = reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadUInt32();
                reader.ReadUInt32();

                byte[] rawName = ReadExact(reader, nameLength);
                byte[] extra = ReadExact(reader, extraLength);
                if (commentLength > 0)
                {
                    ReadExact(reader, commentLength);
                }

                entries.Add(new RawZipEntry(index, flags, rawName, extra));
            }

            return entries;
        }

        private static long FindEndOfCentralDirectory(FileStream stream)
        {
            int searchLength = (int)Math.Min(
                stream.Length,
                MaximumCommentLength + 22L);
            if (searchLength < 22)
            {
                throw new InvalidDataException("The file is too short to be a ZIP archive.");
            }

            byte[] buffer = new byte[searchLength];
            stream.Position = stream.Length - searchLength;
            ReadExact(stream, buffer);
            for (int offset = buffer.Length - 22; offset >= 0; offset--)
            {
                if (ReadUInt32(buffer, offset) != EndOfCentralDirectorySignature)
                {
                    continue;
                }

                ushort commentLength = ReadUInt16(buffer, offset + 20);
                if (offset + 22 + commentLength <= buffer.Length)
                {
                    return stream.Length - searchLength + offset;
                }
            }

            throw new InvalidDataException("ZIP end-of-central-directory record was not found.");
        }

        private static void ReadZip64DirectoryInfo(
            FileStream stream,
            BinaryReader reader,
            long eocdOffset,
            out ulong totalEntries,
            out ulong centralOffset)
        {
            long locatorOffset = eocdOffset - 20;
            if (locatorOffset < 0)
            {
                throw new InvalidDataException("ZIP64 locator was not found.");
            }

            stream.Position = locatorOffset;
            if (reader.ReadUInt32() != Zip64LocatorSignature)
            {
                throw new InvalidDataException("ZIP64 locator is invalid.");
            }

            uint zip64Disk = reader.ReadUInt32();
            ulong zip64RecordOffset = reader.ReadUInt64();
            uint diskCount = reader.ReadUInt32();
            if (zip64Disk != 0 || diskCount != 1 || zip64RecordOffset > (ulong)stream.Length)
            {
                throw new InvalidDataException("Multi-volume ZIP64 archives are not supported.");
            }

            stream.Position = (long)zip64RecordOffset;
            if (reader.ReadUInt32() != Zip64EndOfCentralDirectorySignature)
            {
                throw new InvalidDataException("ZIP64 end-of-central-directory record is invalid.");
            }

            reader.ReadUInt64();
            reader.ReadUInt16();
            reader.ReadUInt16();
            uint diskNumber = reader.ReadUInt32();
            uint centralDisk = reader.ReadUInt32();
            ulong entriesOnDisk = reader.ReadUInt64();
            totalEntries = reader.ReadUInt64();
            reader.ReadUInt64();
            centralOffset = reader.ReadUInt64();
            if (diskNumber != 0 || centralDisk != 0 || entriesOnDisk != totalEntries)
            {
                throw new InvalidDataException("Multi-volume ZIP64 archives are not supported.");
            }
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] data = reader.ReadBytes(count);
            if (data.Length != count)
            {
                throw new EndOfStreamException();
            }

            return data;
        }

        private static void ReadExact(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }
    }

    internal static class Crc32
    {
        private const uint Polynomial = 0xEDB88320;

        public static uint Compute(byte[] data)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0
                        ? (crc >> 1) ^ Polynomial
                        : crc >> 1;
                }
            }

            return ~crc;
        }
    }
}
