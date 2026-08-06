using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using SharpSevenZip;

namespace EditorTweaks.Features.ArchiveIo
{
    internal static class ArchiveService
    {
        private const int MaximumEntryCount = 10000;
        private const ulong MaximumUncompressedBytes = 2097152000;
        private const ushort PeMachineAmd64 = 0x8664;
        private static readonly string[] AdditionalArchiveExtensions =
        {
            "7z",
            "rar",
            "tar",
            "tgz",
            "gz",
            "gzip",
            "tbz",
            "tbz2",
            "bz2",
            "bzip2",
            "txz",
            "xz",
            "cab"
        };
        private static readonly object InitializationLock = new object();
        private static string initializedLibraryPath = string.Empty;
        private static string[]? originalLevelArchiveExtensions;
        private static string[]? originalLevelExtensions;
        private static string[]? installedLevelArchiveExtensions;
        private static string[]? installedLevelExtensions;

        public static void Initialize(string modPath)
        {
            string libraryPath = Path.GetFullPath(
                Path.Combine(modPath, "ThirdParty", "7-Zip", "x64", "7z.dll"));
            lock (InitializationLock)
            {
                if (string.Equals(
                    initializedLibraryPath,
                    libraryPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!Environment.Is64BitProcess)
                {
                    throw new PlatformNotSupportedException(
                        "ArchiveIo requires the 64-bit game process.");
                }

                ValidateNativeLibrary(libraryPath);
                SharpSevenZipBase.SetLibraryPath(libraryPath);
                LibraryFeature features = SharpSevenZipBase.CurrentLibraryFeatures;
                LibraryFeature requiredFeatures =
                    LibraryFeature.ExtractZip
                    | LibraryFeature.Extract7z
                    | LibraryFeature.ExtractRar
                    | LibraryFeature.ExtractGzip
                    | LibraryFeature.ExtractBzip2
                    | LibraryFeature.ExtractTar
                    | LibraryFeature.ExtractXz
                    | LibraryFeature.CompressZip;
                if ((features & requiredFeatures) != requiredFeatures)
                {
                    throw new NotSupportedException(
                        "The bundled 7z.dll does not support the required archive formats.");
                }

                initializedLibraryPath = libraryPath;
                Main.Log("[ArchiveIo] 7-Zip backend initialized: " + libraryPath);
            }
        }

        public static void Extract(string sourceArchive, string destinationDirectory)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(sourceArchive)
                || !File.Exists(sourceArchive))
            {
                throw new FileNotFoundException("Archive file was not found.", sourceArchive);
            }

            string destinationRoot = Path.GetFullPath(destinationDirectory);
            bool createdRoot = !Directory.Exists(destinationRoot);
            List<string> createdFiles = new List<string>();
            HashSet<string> createdDirectories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            try
            {
                Directory.CreateDirectory(destinationRoot);
                if (createdRoot)
                {
                    createdDirectories.Add(destinationRoot);
                }

                if (!SharpSevenZipArchiveFormat.TryCheckFormat(
                    sourceArchive,
                    out ArchiveFormatInfo formatInfo))
                {
                    throw new InvalidDataException(
                        "The file is not a supported archive format.");
                }

                using SharpSevenZipExtractor extractor =
                    new SharpSevenZipExtractor(sourceArchive, formatInfo);
                ValidateArchiveLimits(extractor.ArchiveFileData);
                ZipNameResolution resolution;
                if (formatInfo.Format == InArchiveFormat.Zip)
                {
                    LegacyZipEncodingMode encodingMode = LegacyZipEncodingModes.Parse(
                        Main.Settings.LegacyZipEncoding);
                    resolution = ZipEntryNameResolver.ResolveZip(
                        sourceArchive,
                        extractor,
                        encodingMode);
                }
                else
                {
                    resolution = ZipEntryNameResolver.ResolveNative(
                        sourceArchive,
                        extractor.ArchiveFileData,
                        formatInfo.Format.ToString());
                }

                if (resolution.Entries.Count > MaximumEntryCount)
                {
                    throw new IOException(
                        $"Archive extraction aborted: too many entries ({resolution.Entries.Count} > {MaximumEntryCount}).");
                }

                ulong declaredTotal = 0;
                foreach (ResolvedZipEntry entry in resolution.Entries)
                {
                    declaredTotal = checked(declaredTotal + entry.Size);
                    if (declaredTotal > MaximumUncompressedBytes)
                    {
                        throw new IOException(
                            "Archive extraction aborted: uncompressed size exceeds 2000 MB.");
                    }
                }

                List<ExtractionTarget> targets = resolution.Entries
                    .Select(entry => ResolveExtractionTarget(destinationRoot, entry))
                    .ToList();
                PreflightTargets(targets);

                ulong actualTotal = 0;
                foreach (ExtractionTarget target in targets)
                {
                    if (target.Entry.IsDirectory)
                    {
                        CreateDirectoryTracked(
                            target.FullPath,
                            destinationRoot,
                            createdDirectories);
                        continue;
                    }

                    string? parent = Path.GetDirectoryName(target.FullPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        CreateDirectoryTracked(
                            parent,
                            destinationRoot,
                            createdDirectories);
                    }

                    try
                    {
                        using FileStream output = new FileStream(
                            target.FullPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None);
                        createdFiles.Add(target.FullPath);
                        extractor.ExtractFile(target.Entry.Index, output);
                        actualTotal = checked(actualTotal + (ulong)output.Length);
                        if (actualTotal > MaximumUncompressedBytes)
                        {
                            throw new IOException(
                                "Archive extraction aborted: actual uncompressed size exceeds 2000 MB.");
                        }
                    }
                    catch
                    {
                        TryDeleteFile(target.FullPath);
                        createdFiles.Remove(target.FullPath);
                        throw;
                    }
                }

                TryExtractNestedTar(
                    formatInfo.Format,
                    targets,
                    destinationRoot,
                    createdFiles);

                Main.Log(
                    $"[ArchiveIo] Extracted {resolution.Entries.Count} entries from "
                    + formatInfo.Format
                    + " with filename handling "
                    + resolution.EncodingName
                    + ".");
            }
            catch
            {
                RollBackExtraction(createdFiles, createdDirectories);
                throw;
            }
        }

        private static void TryExtractNestedTar(
            InArchiveFormat outerFormat,
            IReadOnlyList<ExtractionTarget> targets,
            string destinationRoot,
            ICollection<string> createdFiles)
        {
            if (outerFormat != InArchiveFormat.GZip
                && outerFormat != InArchiveFormat.BZip2
                && outerFormat != InArchiveFormat.XZ)
            {
                return;
            }

            List<ExtractionTarget> payloads = targets
                .Where(target => !target.Entry.IsDirectory)
                .Take(2)
                .ToList();
            if (payloads.Count != 1)
            {
                return;
            }

            ExtractionTarget payload = payloads[0];
            if (!SharpSevenZipArchiveFormat.TryCheckFormat(
                    payload.FullPath,
                    out ArchiveFormatInfo nestedFormat)
                || nestedFormat.Format != InArchiveFormat.Tar)
            {
                return;
            }

            Extract(payload.FullPath, destinationRoot);
            TryDeleteFile(payload.FullPath);
            if (!File.Exists(payload.FullPath))
            {
                createdFiles.Remove(payload.FullPath);
            }
        }

        public static void CreateZip(string outputArchive, IReadOnlyList<string> files)
        {
            EnsureInitialized();
            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }

            string outputPath = Path.GetFullPath(outputArchive);
            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new IOException("ZIP output directory is invalid.");
            }

            Directory.CreateDirectory(outputDirectory);
            List<ArchiveInput> inputs = BuildArchiveInputs(files);
            string temporaryPath = outputPath + ".tmp." + Guid.NewGuid().ToString("N");
            Dictionary<string, StreamWithAttributes> streams =
                new Dictionary<string, StreamWithAttributes>(StringComparer.Ordinal);

            try
            {
                foreach (ArchiveInput input in inputs)
                {
                    FileInfo file = new FileInfo(input.SourcePath);
                    FileStream stream = new FileStream(
                        input.SourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                    streams.Add(
                        input.EntryName,
                        new StreamWithAttributes(
                            stream,
                            file.CreationTime,
                            file.LastWriteTime,
                            file.LastAccessTime));
                }

                SharpSevenZipCompressor compressor = new SharpSevenZipCompressor
                {
                    ArchiveFormat = OutArchiveFormat.Zip,
                    CompressionMethod = CompressionMethod.Deflate,
                    CompressionLevel = CompressionLevel.Normal,
                    DirectoryStructure = true
                };
                compressor.CompressStreamDictionary(streams, temporaryPath);

                if (File.Exists(outputPath))
                {
                    File.Replace(temporaryPath, outputPath, null);
                }
                else
                {
                    File.Move(temporaryPath, outputPath);
                }

                Main.Log(
                    $"[ArchiveIo] Created ZIP archive with {inputs.Count} entries: "
                    + outputPath);
            }
            finally
            {
                foreach (StreamWithAttributes value in streams.Values)
                {
                    value.Stream.Dispose();
                }

                TryDeleteFile(temporaryPath);
            }
        }

        public static void EnableAdditionalArchiveExtensions()
        {
            lock (InitializationLock)
            {
                if (installedLevelArchiveExtensions != null)
                {
                    return;
                }

                ref string[] archiveField = ref GetGcsExtensionField(
                    nameof(GCS.levelZipExtensions));
                ref string[] levelField = ref GetGcsExtensionField(
                    nameof(GCS.levelExtensions));
                string[] archiveExtensions = archiveField;
                string[] levelExtensions = levelField;
                string[] expandedArchiveExtensions = archiveExtensions
                    .Concat(AdditionalArchiveExtensions)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                string[] expandedLevelExtensions = levelExtensions
                    .Concat(AdditionalArchiveExtensions)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                archiveField = expandedArchiveExtensions;
                try
                {
                    levelField = expandedLevelExtensions;
                }
                catch
                {
                    archiveField = archiveExtensions;
                    throw;
                }

                originalLevelArchiveExtensions = archiveExtensions;
                originalLevelExtensions = levelExtensions;
                installedLevelArchiveExtensions = expandedArchiveExtensions;
                installedLevelExtensions = expandedLevelExtensions;
                Main.Log(
                    "[ArchiveIo] Enabled level archive extensions: "
                    + string.Join(", ", expandedArchiveExtensions));
            }
        }

        public static void DisableAdditionalArchiveExtensions()
        {
            lock (InitializationLock)
            {
                if (installedLevelArchiveExtensions == null)
                {
                    return;
                }

                ref string[] archiveField = ref GetGcsExtensionField(
                    nameof(GCS.levelZipExtensions));
                ref string[] levelField = ref GetGcsExtensionField(
                    nameof(GCS.levelExtensions));
                if (ReferenceEquals(
                    archiveField,
                    installedLevelArchiveExtensions))
                {
                    archiveField = originalLevelArchiveExtensions!;
                }

                if (ReferenceEquals(
                    levelField,
                    installedLevelExtensions))
                {
                    levelField = originalLevelExtensions!;
                }

                originalLevelArchiveExtensions = null;
                originalLevelExtensions = null;
                installedLevelArchiveExtensions = null;
                installedLevelExtensions = null;
            }
        }

        private static List<ArchiveInput> BuildArchiveInputs(
            IReadOnlyList<string> files)
        {
            string levelRoot = string.IsNullOrWhiteSpace(ADOBase.levelPath)
                ? string.Empty
                : Path.GetDirectoryName(Path.GetFullPath(ADOBase.levelPath)) ?? string.Empty;
            string tempRoot = Path.GetFullPath(
                Path.Combine(Persistence.DataPath, "Temp"));
            List<ArchiveInput> inputs = new List<ArchiveInput>();
            HashSet<string> entryNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (string rawPath in files)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string sourcePath = Path.GetFullPath(rawPath);
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        "Archive input file was not found.",
                        sourcePath);
                }

                string entryName;
                string? parent = Path.GetDirectoryName(sourcePath);
                if (string.Equals(
                        Path.GetExtension(sourcePath),
                        ".adofai",
                        StringComparison.OrdinalIgnoreCase)
                    && parent != null
                    && IsWithinRoot(parent, tempRoot))
                {
                    entryName = Path.GetFileName(sourcePath);
                }
                else if (!string.IsNullOrEmpty(levelRoot)
                    && IsWithinRoot(sourcePath, levelRoot))
                {
                    entryName = MakeRelativePath(levelRoot, sourcePath);
                }
                else
                {
                    entryName = Path.GetFileName(sourcePath);
                }

                entryName = NormalizeArchiveEntryName(entryName);
                if (!entryNames.Add(entryName))
                {
                    throw new IOException(
                        "Archive contains duplicate output entry: " + entryName);
                }

                inputs.Add(new ArchiveInput(sourcePath, entryName));
            }

            return inputs;
        }

        private static ref string[] GetGcsExtensionField(string name)
        {
            return ref AccessTools.StaticFieldRefAccess<GCS, string[]>(name);
        }

        private static void ValidateArchiveLimits(
            IReadOnlyList<ArchiveFileInfo> entries)
        {
            if (entries.Count > MaximumEntryCount)
            {
                throw new IOException(
                    $"Archive extraction aborted: too many entries ({entries.Count} > {MaximumEntryCount}).");
            }

            ulong declaredTotal = 0;
            foreach (ArchiveFileInfo entry in entries)
            {
                declaredTotal = checked(declaredTotal + entry.Size);
                if (declaredTotal > MaximumUncompressedBytes)
                {
                    throw new IOException(
                        "Archive extraction aborted: uncompressed size exceeds 2000 MB.");
                }
            }
        }

        private static ExtractionTarget ResolveExtractionTarget(
            string destinationRoot,
            ResolvedZipEntry entry)
        {
            string localPath = entry.RelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(
                Path.Combine(destinationRoot, localPath));
            string rootPrefix = EnsureTrailingSeparator(destinationRoot);
            if (!fullPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "Archive entry resolves outside the destination directory: "
                    + entry.RelativePath);
            }

            return new ExtractionTarget(entry, fullPath);
        }

        private static void PreflightTargets(IEnumerable<ExtractionTarget> targets)
        {
            foreach (ExtractionTarget target in targets)
            {
                if (target.Entry.IsDirectory)
                {
                    if (File.Exists(target.FullPath))
                    {
                        throw new IOException(
                            "Archive directory collides with an existing file: "
                            + target.Entry.RelativePath);
                    }
                }
                else if (File.Exists(target.FullPath)
                    || Directory.Exists(target.FullPath))
                {
                    throw new IOException(
                        "Archive file already exists at the destination: "
                        + target.Entry.RelativePath);
                }
            }
        }

        private static void CreateDirectoryTracked(
            string directory,
            string destinationRoot,
            ISet<string> createdDirectories)
        {
            Stack<string> missing = new Stack<string>();
            string? current = directory;
            while (!string.IsNullOrEmpty(current)
                && !Directory.Exists(current)
                && IsWithinRoot(current, destinationRoot))
            {
                missing.Push(current);
                current = Path.GetDirectoryName(current);
            }

            while (missing.Count > 0)
            {
                string path = missing.Pop();
                Directory.CreateDirectory(path);
                createdDirectories.Add(path);
            }
        }

        private static void RollBackExtraction(
            IEnumerable<string> createdFiles,
            IEnumerable<string> createdDirectories)
        {
            foreach (string file in createdFiles.Reverse())
            {
                TryDeleteFile(file);
            }

            foreach (string directory in createdDirectories
                .OrderByDescending(path => path.Length))
            {
                try
                {
                    if (Directory.Exists(directory)
                        && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (Exception exception)
                {
                    Main.Log(
                        "[ArchiveIo] Failed to remove extraction directory: "
                        + exception.GetBaseException().Message);
                }
            }
        }

        private static string MakeRelativePath(string root, string file)
        {
            Uri rootUri = new Uri(EnsureTrailingSeparator(root));
            Uri fileUri = new Uri(file);
            return Uri.UnescapeDataString(
                rootUri.MakeRelativeUri(fileUri).ToString());
        }

        private static string NormalizeArchiveEntryName(string name)
        {
            string normalized = name.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.Split('/').Any(segment =>
                    string.IsNullOrEmpty(segment)
                    || segment == "."
                    || segment == ".."))
            {
                throw new IOException("Archive output entry is invalid: " + name);
            }

            return normalized;
        }

        private static bool IsWithinRoot(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root);
            return string.Equals(
                    fullPath,
                    fullRoot,
                    StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(
                    EnsureTrailingSeparator(fullRoot),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static void EnsureInitialized()
        {
            if (string.IsNullOrEmpty(initializedLibraryPath))
            {
                throw new InvalidOperationException(
                    "The 7-Zip backend has not been initialized.");
            }
        }

        private static void ValidateNativeLibrary(string libraryPath)
        {
            if (!File.Exists(libraryPath))
            {
                throw new FileNotFoundException(
                    "The bundled x64 7z.dll is missing.",
                    libraryPath);
            }

            using FileStream stream = new FileStream(
                libraryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using BinaryReader reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D)
            {
                throw new BadImageFormatException("7z.dll is not a valid PE image.");
            }

            stream.Position = 0x3C;
            int peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset + 6 > stream.Length)
            {
                throw new BadImageFormatException("7z.dll has an invalid PE header.");
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550
                || reader.ReadUInt16() != PeMachineAmd64)
            {
                throw new BadImageFormatException(
                    "7z.dll is not the required x64 build.");
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Main.Log(
                    "[ArchiveIo] Failed to delete temporary file: "
                    + exception.GetBaseException().Message);
            }
        }

        private sealed class ExtractionTarget
        {
            public ExtractionTarget(ResolvedZipEntry entry, string fullPath)
            {
                Entry = entry;
                FullPath = fullPath;
            }

            public ResolvedZipEntry Entry { get; }

            public string FullPath { get; }
        }

        private sealed class ArchiveInput
        {
            public ArchiveInput(string sourcePath, string entryName)
            {
                SourcePath = sourcePath;
                EntryName = entryName;
            }

            public string SourcePath { get; }

            public string EntryName { get; }
        }
    }
}
