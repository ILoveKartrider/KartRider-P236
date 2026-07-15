// Adapted from yanygm/Launcher_V2 RhoArchive (AFL-3.0). See ../NOTICE.md.
using System.Globalization;
using System.Text;

namespace KartRider.P236.ItemProbabilities.Internal;

internal enum RhoFileProperty
{
    None = 0x00,
    Compressed = 0x01,
    Encrypted = 0x04,
    PartialEncrypted = 0x05,
    CompressedEncrypted = 0x06,
}

[Flags]
internal enum RhoBlockProperty
{
    None = 0,
    Compressed = 2,
    PartialEncrypted = 4,
    FullEncrypted = 5,
    CompressedEncrypted = FullEncrypted | Compressed,
}

internal sealed class RhoArchiveFile
{
    internal RhoArchiveFile(string name, byte[] data, RhoFileProperty property = RhoFileProperty.None)
    {
        Name = name;
        Data = data;
        Property = property;
    }

    internal string Name { get; }

    internal byte[] Data { get; set; }

    internal RhoFileProperty Property { get; }

    internal string NameWithoutExtension
    {
        get
        {
            int separator = Name.LastIndexOf('.');
            return separator > 0 ? Name[..separator] : Name;
        }
    }

    internal uint Extension
    {
        get
        {
            int separator = Name.LastIndexOf('.');
            string extension = separator >= 0 ? Name[(separator + 1)..] : string.Empty;
            byte[] bytes = new byte[sizeof(uint)];
            Encoding.ASCII.GetBytes(extension.AsSpan(0, Math.Min(extension.Length, bytes.Length)), bytes);
            return BitConverter.ToUInt32(bytes);
        }
    }
}

internal sealed class RhoArchiveFolder
{
    internal RhoArchiveFolder(string name, RhoArchiveFolder? parent = null)
    {
        Name = name;
        Parent = parent;
    }

    internal string Name { get; }

    internal RhoArchiveFolder? Parent { get; }

    internal List<RhoArchiveFolder> Folders { get; } = new();

    internal List<RhoArchiveFile> Files { get; } = new();

    internal string FullName => Parent is null || string.IsNullOrEmpty(Parent.FullName)
        ? Name
        : $"{Parent.FullName}/{Name}";

    internal RhoArchiveFolder AddFolder(string name)
    {
        if (Folders.Any(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"RHO folder already contains a child named '{name}'.");
        }

        RhoArchiveFolder folder = new(name, this);
        Folders.Add(folder);
        return folder;
    }

    internal void AddFile(RhoArchiveFile file)
    {
        if (Files.Any(candidate => string.Equals(candidate.Name, file.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"RHO folder already contains a file named '{file.Name}'.");
        }

        Files.Add(file);
    }

    internal RhoArchiveFile? GetFile(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        RhoArchiveFolder current = this;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            RhoArchiveFolder? next = current.Folders.FirstOrDefault(folder =>
                string.Equals(folder.Name, parts[index], StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current.Files.FirstOrDefault(file =>
            string.Equals(file.Name, parts[^1], StringComparison.OrdinalIgnoreCase));
    }
}

internal readonly record struct RhoArchiveMetadata(uint Key, uint DataHash, long MediaSize);

internal sealed class RhoArchiveDocument
{
    private const int MaximumBlocks = 2_000_000;
    private const int MaximumFolderEntries = 1_000_000;
    private const int MaximumNameCharacters = 32_768;
    private const int MaximumDecodedBlockBytes = 512 * 1024 * 1024;
    private const uint DataInfoWhiteningKey = 0x3a9213ac;
    private const uint HeaderEndMagic = 0xfc1f9778;
    private static readonly string[] LayerIdentifiers = { "Rh layer spec 1.0", "Rh layer spec 1.1" };
    private const string LayerSecondText = "KartRider (veblush & dew)";

    private RhoArchiveDocument(int layerVersion)
    {
        if (layerVersion is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(layerVersion));
        }

        LayerVersion = layerVersion;
        Root = new RhoArchiveFolder(string.Empty);
    }

    internal int LayerVersion { get; }

    internal RhoArchiveFolder Root { get; }

    internal void ValidateSemanticallyEqual(RhoArchiveDocument other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (LayerVersion != other.LayerVersion)
        {
            throw new InvalidDataException(
                $"RHO layer version mismatch: expected {LayerVersion.ToString(CultureInfo.InvariantCulture)}, " +
                $"actual {other.LayerVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        ValidateFolderSemanticallyEqual(Root, other.Root, "/");
    }

    internal static RhoArchiveDocument Create(int layerVersion = 1) => new(layerVersion);

    private static void ValidateFolderSemanticallyEqual(
        RhoArchiveFolder expected,
        RhoArchiveFolder actual,
        string path)
    {
        if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"RHO folder name mismatch at '{path}': expected '{expected.Name}', actual '{actual.Name}'.");
        }

        if (expected.Files.Count != actual.Files.Count)
        {
            throw new InvalidDataException(
                $"RHO file count mismatch at '{path}': expected " +
                $"{expected.Files.Count.ToString(CultureInfo.InvariantCulture)}, actual " +
                $"{actual.Files.Count.ToString(CultureInfo.InvariantCulture)}.");
        }

        for (int index = 0; index < expected.Files.Count; index++)
        {
            RhoArchiveFile expectedFile = expected.Files[index];
            RhoArchiveFile actualFile = actual.Files[index];
            string filePath = path == "/"
                ? $"/{expectedFile.Name}"
                : $"{path}/{expectedFile.Name}";
            if (!string.Equals(expectedFile.Name, actualFile.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"RHO file name or order mismatch at '{path}' index " +
                    $"{index.ToString(CultureInfo.InvariantCulture)}: expected '{expectedFile.Name}', " +
                    $"actual '{actualFile.Name}'.");
            }

            if (expectedFile.Property != actualFile.Property)
            {
                throw new InvalidDataException(
                    $"RHO file property mismatch at '{filePath}': expected {expectedFile.Property}, " +
                    $"actual {actualFile.Property}.");
            }

            if (!expectedFile.Data.AsSpan().SequenceEqual(actualFile.Data))
            {
                throw new InvalidDataException($"RHO decoded file data mismatch at '{filePath}'.");
            }
        }

        if (expected.Folders.Count != actual.Folders.Count)
        {
            throw new InvalidDataException(
                $"RHO child folder count mismatch at '{path}': expected " +
                $"{expected.Folders.Count.ToString(CultureInfo.InvariantCulture)}, actual " +
                $"{actual.Folders.Count.ToString(CultureInfo.InvariantCulture)}.");
        }

        for (int index = 0; index < expected.Folders.Count; index++)
        {
            RhoArchiveFolder expectedFolder = expected.Folders[index];
            RhoArchiveFolder actualFolder = actual.Folders[index];
            string folderPath = path == "/"
                ? $"/{expectedFolder.Name}"
                : $"{path}/{expectedFolder.Name}";
            if (!string.Equals(expectedFolder.Name, actualFolder.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"RHO child folder name or order mismatch at '{path}' index " +
                    $"{index.ToString(CultureInfo.InvariantCulture)}: expected '{expectedFolder.Name}', " +
                    $"actual '{actualFolder.Name}'.");
            }

            ValidateFolderSemanticallyEqual(expectedFolder, actualFolder, folderPath);
        }
    }

    internal static RhoArchiveDocument Open(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, Encoding.Unicode, leaveOpen: true);
        if (stream.Length < 0x100)
        {
            throw new InvalidDataException("RHO archive is shorter than its header.");
        }

        byte[] identifierBytes = ReadExactly(reader, 0x40);
        string identifier = Encoding.Unicode.GetString(identifierBytes).TrimEnd('\0');
        int layerVersion = Array.FindIndex(LayerIdentifiers, candidate => identifier.StartsWith(candidate, StringComparison.Ordinal));
        if (layerVersion < 0)
        {
            throw new InvalidDataException("RHO layer identifier is unsupported.");
        }

        uint rhoKey = RhoCrypto.GetRhoKey(Path.GetFileNameWithoutExtension(fullPath));
        stream.Position = 0x80;
        byte[] encryptedHeader = ReadExactly(reader, 0x80);
        byte[] header = encryptedHeader.ToArray();
        if (layerVersion == 0)
        {
            RhoCrypto.TransformData(rhoKey, header);
        }
        else
        {
            header = RhoCrypto.TransformHeader(header, rhoKey, encrypt: false);
        }

        uint expectedHeaderHash = BitConverter.ToUInt32(header, 0);
        if (Adler32.Compute(0, header.AsSpan(4, 0x7c)) != expectedHeaderHash)
        {
            throw new InvalidDataException("RHO header checksum does not match.");
        }

        int blockCount;
        uint blockInfoKey;
        byte[] oldBlockInfoKey;
        using (MemoryStream headerStream = new(header, writable: false))
        using (BinaryReader headerReader = new(headerStream))
        {
            _ = headerReader.ReadUInt32();
            int versionCode = headerReader.ReadInt32();
            if (versionCode != (0x10000 | layerVersion))
            {
                throw new InvalidDataException("RHO version code does not match its layer identifier.");
            }

            blockCount = headerReader.ReadInt32();
            if (blockCount is <= 0 or > MaximumBlocks)
            {
                throw new InvalidDataException("RHO block count is outside the supported range.");
            }

            uint whiteningKey = headerReader.ReadUInt32();
            blockInfoKey = whiteningKey ^ rhoKey;
            if (layerVersion == 0)
            {
                oldBlockInfoKey = ReadExactly(headerReader, 32);
            }
            else
            {
                oldBlockInfoKey = Array.Empty<byte>();
                _ = headerReader.ReadInt32();
                _ = headerReader.ReadInt32();
                _ = headerReader.ReadUInt32();
            }

            if (headerReader.ReadUInt32() != HeaderEndMagic)
            {
                throw new InvalidDataException("RHO header end marker is invalid.");
            }
        }

        long blockInfoEnd = checked(0x100L + ((long)blockCount * 0x20));
        if (blockInfoEnd > stream.Length)
        {
            throw new InvalidDataException("RHO block table is truncated.");
        }

        stream.Position = 0x100;
        Dictionary<uint, RhoBlockInfo> blocks = new(blockCount);
        for (int index = 0; index < blockCount; index++)
        {
            byte[] encoded = ReadExactly(reader, 0x20);
            byte[] decoded = layerVersion == 0
                ? RhoCrypto.TransformOldBlockInfo(encoded, oldBlockInfoKey)
                : RhoCrypto.TransformHeader(encoded, blockInfoKey++, encrypt: false);
            RhoBlockInfo info = RhoBlockInfo.Read(decoded);
            info.Validate(stream.Length);
            if (!blocks.TryAdd(info.Index, info))
            {
                throw new InvalidDataException("RHO contains duplicate block indexes.");
            }
        }

        RhoArchiveDocument archive = new(layerVersion);
        Queue<(uint Index, RhoArchiveFolder Folder)> folders = new();
        HashSet<uint> visitedFolders = new();
        folders.Enqueue((uint.MaxValue, archive.Root));
        uint folderKey = RhoCrypto.GetDirectoryDataKey(rhoKey);

        while (folders.Count > 0)
        {
            (uint folderIndex, RhoArchiveFolder folder) = folders.Dequeue();
            if (!visitedFolders.Add(folderIndex))
            {
                throw new InvalidDataException("RHO folder graph contains a cycle or duplicate index.");
            }

            byte[] folderData = ReadBlock(stream, blocks, folderIndex, folderKey);
            using MemoryStream folderStream = new(folderData, writable: false);
            using BinaryReader folderReader = new(folderStream, Encoding.Unicode, leaveOpen: true);
            int folderCount = ReadEntryCount(folderReader, "folder");
            for (int index = 0; index < folderCount; index++)
            {
                string name = ReadNullTerminatedUnicode(folderReader);
                uint childIndex = folderReader.ReadUInt32();
                RhoArchiveFolder child = folder.AddFolder(name);
                folders.Enqueue((childIndex, child));
            }

            int fileCount = ReadEntryCount(folderReader, "file");
            for (int index = 0; index < fileCount; index++)
            {
                string baseName = ReadNullTerminatedUnicode(folderReader);
                uint extension = folderReader.ReadUInt32();
                RhoFileProperty property = (RhoFileProperty)folderReader.ReadInt32();
                if (!Enum.IsDefined(property))
                {
                    throw new InvalidDataException("RHO file has an unsupported storage property.");
                }

                uint dataIndex = folderReader.ReadUInt32();
                int fileSize = folderReader.ReadInt32();
                if (fileSize < 0 || fileSize > MaximumDecodedBlockBytes)
                {
                    throw new InvalidDataException("RHO file declares an invalid size.");
                }

                uint fileKey = RhoCrypto.GetFileKey(rhoKey, baseName, extension);
                byte[] data = ReadBlock(stream, blocks, dataIndex, fileKey);
                if (data.Length != fileSize)
                {
                    throw new InvalidDataException("RHO file size does not match its decoded block data.");
                }

                string extensionName = Encoding.ASCII.GetString(BitConverter.GetBytes(extension)).TrimEnd('\0');
                string name = string.IsNullOrEmpty(extensionName) ? baseName : $"{baseName}.{extensionName}";
                folder.AddFile(new RhoArchiveFile(name, data, property));
            }

            if (folderStream.Position != folderStream.Length)
            {
                throw new InvalidDataException("RHO folder metadata contains trailing bytes.");
            }
        }

        return archive;
    }

    internal RhoArchiveMetadata Save(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("RHO destination directory does not exist.");
        }

        uint rhoKey = RhoCrypto.GetRhoKey(Path.GetFileNameWithoutExtension(fullPath));
        HashSet<uint> usedIndexes = new();
        Dictionary<RhoArchiveFolder, uint> folderIndexes = AssignFolderIndexes(usedIndexes);
        List<RhoSavingBlock> blocks = new();
        Dictionary<RhoArchiveFile, uint> fileIndexes = new();
        BuildFolderBlocks(Root, rhoKey, folderIndexes, fileIndexes, usedIndexes, blocks);
        if (blocks.Count is <= 0 or > MaximumBlocks)
        {
            throw new InvalidDataException("Generated RHO block count is outside the supported range.");
        }

        uint dataHash = 0;
        foreach (RhoSavingBlock block in blocks)
        {
            dataHash = Adler32.Continue(dataHash, block.Data);
        }

        int blockTableSize = Align256(checked(blocks.Count * 0x20));
        long dataStart = checked(0x100L + blockTableSize);
        long relativeOffset = 0;
        foreach (RhoSavingBlock block in blocks)
        {
            block.Info.Offset = checked(dataStart + relativeOffset);
            relativeOffset = Align256(checked(relativeOffset + block.Data.Length));
        }

        using (FileStream stream = new(
                   fullPath,
                   FileMode.Create,
                   FileAccess.ReadWrite,
                   FileShare.None,
                   64 * 1024,
                   FileOptions.WriteThrough))
        using (BinaryWriter writer = new(stream, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(Encoding.Unicode.GetBytes(LayerIdentifiers[LayerVersion]));
            stream.Position = 0x40;
            writer.Write(Encoding.Unicode.GetBytes(LayerSecondText));
            stream.Position = 0x80;
            writer.Write(BuildHeader(rhoKey, blocks.Count, dataHash));

            stream.Position = 0x100;
            uint currentBlockKey = DataInfoWhiteningKey ^ rhoKey;
            byte[] oldBlockKey = new byte[32];
            foreach (RhoSavingBlock block in blocks)
            {
                byte[] metadata = block.Info.Write();
                metadata = LayerVersion == 0
                    ? RhoCrypto.TransformOldBlockInfo(metadata, oldBlockKey)
                    : RhoCrypto.TransformHeader(metadata, currentBlockKey++, encrypt: true);
                writer.Write(metadata);
            }

            foreach (RhoSavingBlock block in blocks)
            {
                stream.Position = block.Info.Offset;
                writer.Write(block.Data);
            }

            long finalLength = checked(dataStart + relativeOffset);
            stream.SetLength(finalLength);
            stream.Flush(flushToDisk: true);
        }

        return new RhoArchiveMetadata(rhoKey, LayerVersion == 1 ? dataHash : 0, new FileInfo(fullPath).Length);
    }

    internal static RhoArchiveMetadata ReadMetadata(string path)
    {
        string fullPath = Path.GetFullPath(path);
        uint rhoKey = RhoCrypto.GetRhoKey(Path.GetFileNameWithoutExtension(fullPath));
        using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream);
        stream.Position = 0;
        string identifier = Encoding.Unicode.GetString(ReadExactly(reader, 0x40)).TrimEnd('\0');
        int layerVersion = Array.FindIndex(LayerIdentifiers, candidate => identifier.StartsWith(candidate, StringComparison.Ordinal));
        if (layerVersion < 0)
        {
            throw new InvalidDataException("RHO layer identifier is unsupported.");
        }

        stream.Position = 0x80;
        byte[] header = ReadExactly(reader, 0x80);
        if (layerVersion == 0)
        {
            RhoCrypto.TransformData(rhoKey, header);
        }
        else
        {
            header = RhoCrypto.TransformHeader(header, rhoKey, encrypt: false);
        }

        uint dataHash = layerVersion == 1 ? BitConverter.ToUInt32(header, 24) : 0;
        return new RhoArchiveMetadata(rhoKey, dataHash, stream.Length);
    }

    private Dictionary<RhoArchiveFolder, uint> AssignFolderIndexes(HashSet<uint> usedIndexes)
    {
        Dictionary<RhoArchiveFolder, uint> indexes = new();
        Queue<RhoArchiveFolder> folders = new();
        folders.Enqueue(Root);
        while (folders.Count > 0)
        {
            RhoArchiveFolder folder = folders.Dequeue();
            uint index = folder.Parent is null
                ? uint.MaxValue
                : Adler32.Compute(0, Encoding.Unicode.GetBytes(folder.FullName));
            while (!usedIndexes.Add(index))
            {
                index = unchecked(index + 0x5f03e367u);
            }

            indexes.Add(folder, index);
            foreach (RhoArchiveFolder child in folder.Folders)
            {
                folders.Enqueue(child);
            }
        }

        return indexes;
    }

    private static void BuildFolderBlocks(
        RhoArchiveFolder folder,
        uint rhoKey,
        IReadOnlyDictionary<RhoArchiveFolder, uint> folderIndexes,
        Dictionary<RhoArchiveFile, uint> fileIndexes,
        HashSet<uint> usedIndexes,
        ICollection<RhoSavingBlock> output)
    {
        uint folderIndex = folderIndexes[folder];
        foreach (RhoArchiveFile file in folder.Files)
        {
            ValidateArchiveName(file.Name, "file");
            uint fileIndex = unchecked(
                Adler32.Compute(0, Encoding.Unicode.GetBytes(file.NameWithoutExtension)) +
                file.Extension +
                (folderIndex == uint.MaxValue ? 0 : folderIndex));
            while (usedIndexes.Contains(fileIndex) || usedIndexes.Contains(unchecked(fileIndex + 1)))
            {
                fileIndex = unchecked(fileIndex + 0x4d21cb4fu);
            }

            usedIndexes.Add(fileIndex);
            fileIndexes.Add(file, fileIndex);
            AddFileBlocks(file, fileIndex, rhoKey, output, usedIndexes);
        }

        foreach (RhoArchiveFolder child in folder.Folders)
        {
            ValidateArchiveName(child.Name, "folder");
            BuildFolderBlocks(child, rhoKey, folderIndexes, fileIndexes, usedIndexes, output);
        }

        byte[] metadata = BuildFolderMetadata(folder, folderIndexes, fileIndexes);
        uint checksum = Adler32.Compute(0, metadata);
        RhoCrypto.TransformData(RhoCrypto.GetDirectoryDataKey(rhoKey), metadata);
        output.Add(new RhoSavingBlock(
            new RhoBlockInfo
            {
                Index = folderIndex,
                DataSize = metadata.Length,
                UncompressedSize = metadata.Length,
                Property = RhoBlockProperty.FullEncrypted,
                Checksum = checksum,
            },
            metadata));
    }

    private static void AddFileBlocks(
        RhoArchiveFile file,
        uint fileIndex,
        uint rhoKey,
        ICollection<RhoSavingBlock> output,
        HashSet<uint> usedIndexes)
    {
        if (file.Data.Length > MaximumDecodedBlockBytes)
        {
            throw new InvalidDataException("RHO file data exceeds the supported size.");
        }

        byte[] storedData = file.Data.ToArray();
        uint checksum = 0;
        uint fileKey = RhoCrypto.GetFileKey(rhoKey, file.NameWithoutExtension, file.Extension);
        if (file.Property is RhoFileProperty.Encrypted or RhoFileProperty.CompressedEncrypted)
        {
            checksum = Adler32.Compute(0, storedData);
            RhoCrypto.TransformData(fileKey, storedData);
        }
        else if (file.Property == RhoFileProperty.PartialEncrypted)
        {
            RhoCrypto.TransformData(fileKey, storedData.AsSpan(0, Math.Min(0x100, storedData.Length)));
        }

        if (file.Property is RhoFileProperty.Compressed or RhoFileProperty.CompressedEncrypted)
        {
            storedData = KrDataCodec.Compress(storedData);
        }

        if (file.Property == RhoFileProperty.PartialEncrypted)
        {
            int firstLength = Math.Min(0x100, storedData.Length);
            output.Add(new RhoSavingBlock(
                new RhoBlockInfo
                {
                    Index = fileIndex,
                    DataSize = firstLength,
                    UncompressedSize = firstLength,
                    Property = RhoBlockProperty.PartialEncrypted,
                },
                storedData[..firstLength]));
            if (storedData.Length > firstLength)
            {
                uint secondIndex = unchecked(fileIndex + 1);
                usedIndexes.Add(secondIndex);
                output.Add(new RhoSavingBlock(
                    new RhoBlockInfo
                    {
                        Index = secondIndex,
                        DataSize = storedData.Length - firstLength,
                        UncompressedSize = storedData.Length - firstLength,
                        Property = RhoBlockProperty.None,
                    },
                    storedData[firstLength..]));
            }

            return;
        }

        RhoBlockProperty blockProperty = file.Property switch
        {
            RhoFileProperty.None => RhoBlockProperty.None,
            RhoFileProperty.Compressed => RhoBlockProperty.Compressed,
            RhoFileProperty.Encrypted => RhoBlockProperty.FullEncrypted,
            RhoFileProperty.CompressedEncrypted => RhoBlockProperty.CompressedEncrypted,
            _ => throw new InvalidDataException("RHO file property is unsupported."),
        };
        output.Add(new RhoSavingBlock(
            new RhoBlockInfo
            {
                Index = fileIndex,
                DataSize = storedData.Length,
                UncompressedSize = file.Data.Length,
                Property = blockProperty,
                Checksum = checksum,
            },
            storedData));
    }

    private static byte[] BuildFolderMetadata(
        RhoArchiveFolder folder,
        IReadOnlyDictionary<RhoArchiveFolder, uint> folderIndexes,
        IReadOnlyDictionary<RhoArchiveFile, uint> fileIndexes)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(folder.Folders.Count);
            foreach (RhoArchiveFolder child in folder.Folders)
            {
                WriteNullTerminatedUnicode(writer, child.Name);
                writer.Write(folderIndexes[child]);
            }

            writer.Write(folder.Files.Count);
            foreach (RhoArchiveFile file in folder.Files)
            {
                WriteNullTerminatedUnicode(writer, file.NameWithoutExtension);
                writer.Write(file.Extension);
                writer.Write((int)file.Property);
                writer.Write(fileIndexes[file]);
                writer.Write(file.Data.Length);
            }
        }

        return stream.ToArray();
    }

    private byte[] BuildHeader(uint rhoKey, int blockCount, uint dataHash)
    {
        byte[] header = new byte[0x80];
        using (MemoryStream stream = new(header, writable: true))
        using (BinaryWriter writer = new(stream))
        {
            stream.Position = 4;
            writer.Write(0x10000 | LayerVersion);
            writer.Write(blockCount);
            writer.Write(DataInfoWhiteningKey);
            if (LayerVersion == 0)
            {
                writer.Write(new byte[32]);
            }
            else
            {
                writer.Write(1);
                writer.Write(unchecked(rhoKey - 0x397e40c3u));
                writer.Write(dataHash);
            }

            writer.Write(HeaderEndMagic);
            writer.Write(0x7e);
        }

        BitConverter.GetBytes(Adler32.Compute(0, header.AsSpan(4, 0x7c))).CopyTo(header, 0);
        if (LayerVersion == 0)
        {
            RhoCrypto.TransformData(rhoKey, header);
            return header;
        }

        return RhoCrypto.TransformHeader(header, rhoKey, encrypt: true);
    }

    private static byte[] ReadBlock(
        FileStream stream,
        IReadOnlyDictionary<uint, RhoBlockInfo> blocks,
        uint index,
        uint key)
    {
        if (!blocks.TryGetValue(index, out RhoBlockInfo? info))
        {
            throw new InvalidDataException(
                $"RHO references missing block {index.ToString(CultureInfo.InvariantCulture)}.");
        }

        byte[] stored = ReadBlockBytes(stream, info);
        byte[] decoded = (info.Property & RhoBlockProperty.Compressed) != 0
            ? KrDataCodec.Decompress(stored, info.UncompressedSize)
            : stored;
        if ((info.Property & RhoBlockProperty.PartialEncrypted) != 0)
        {
            RhoCrypto.TransformData(key, decoded);
        }

        if (info.Property == RhoBlockProperty.PartialEncrypted &&
            blocks.TryGetValue(unchecked(index + 1), out RhoBlockInfo? second))
        {
            byte[] tail = ReadBlockBytes(stream, second);
            int originalLength = decoded.Length;
            Array.Resize(ref decoded, checked(originalLength + tail.Length));
            tail.CopyTo(decoded, originalLength);
        }

        if (info.Checksum != 0 && Adler32.Compute(0, decoded) != info.Checksum)
        {
            throw new InvalidDataException("RHO decoded block checksum does not match.");
        }

        return decoded;
    }

    private static byte[] ReadBlockBytes(FileStream stream, RhoBlockInfo info)
    {
        stream.Position = info.Offset;
        byte[] data = new byte[info.DataSize];
        stream.ReadExactly(data);
        return data;
    }

    private static int ReadEntryCount(BinaryReader reader, string kind)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > MaximumFolderEntries)
        {
            throw new InvalidDataException($"RHO {kind} count is invalid.");
        }

        return count;
    }

    private static string ReadNullTerminatedUnicode(BinaryReader reader)
    {
        StringBuilder builder = new();
        for (int count = 0; count <= MaximumNameCharacters; count++)
        {
            char character = (char)reader.ReadUInt16();
            if (character == '\0')
            {
                string value = builder.ToString();
                ValidateArchiveName(value, "entry");
                return value;
            }

            builder.Append(character);
        }

        throw new InvalidDataException("RHO entry name exceeds the supported length.");
    }

    private static void WriteNullTerminatedUnicode(BinaryWriter writer, string value)
    {
        ValidateArchiveName(value, "entry");
        writer.Write(Encoding.Unicode.GetBytes(value));
        writer.Write((ushort)0);
    }

    private static void ValidateArchiveName(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumNameCharacters ||
            value.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
        {
            throw new InvalidDataException($"RHO {kind} name is invalid.");
        }
    }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        byte[] data = reader.ReadBytes(count);
        if (data.Length != count)
        {
            throw new EndOfStreamException("RHO data is truncated.");
        }

        return data;
    }

    private static int Align256(int value) => checked((value + 0xff) & ~0xff);

    private static long Align256(long value) => checked((value + 0xffL) & ~0xffL);

    private sealed class RhoSavingBlock
    {
        internal RhoSavingBlock(RhoBlockInfo info, byte[] data)
        {
            Info = info;
            Data = data;
        }

        internal RhoBlockInfo Info { get; }

        internal byte[] Data { get; }
    }

    private sealed class RhoBlockInfo
    {
        internal uint Index { get; set; }

        internal long Offset { get; set; }

        internal int DataSize { get; set; }

        internal int UncompressedSize { get; set; }

        internal RhoBlockProperty Property { get; set; }

        internal uint Checksum { get; set; }

        internal static RhoBlockInfo Read(ReadOnlySpan<byte> data)
        {
            using MemoryStream stream = new(data.ToArray(), writable: false);
            using BinaryReader reader = new(stream);
            return new RhoBlockInfo
            {
                Index = reader.ReadUInt32(),
                Offset = checked((long)reader.ReadUInt32() << 8),
                DataSize = reader.ReadInt32(),
                UncompressedSize = reader.ReadInt32(),
                Property = (RhoBlockProperty)reader.ReadInt32(),
                Checksum = reader.ReadUInt32(),
            };
        }

        internal byte[] Write()
        {
            if ((Offset & 0xff) != 0 || Offset < 0 || (Offset >> 8) > uint.MaxValue)
            {
                throw new InvalidDataException("RHO block offset cannot be represented.");
            }

            byte[] data = new byte[0x20];
            using MemoryStream stream = new(data, writable: true);
            using BinaryWriter writer = new(stream);
            writer.Write(Index);
            writer.Write((uint)(Offset >> 8));
            writer.Write(DataSize);
            writer.Write(UncompressedSize);
            writer.Write((int)Property);
            writer.Write(Checksum);
            return data;
        }

        internal void Validate(long archiveLength)
        {
            if (DataSize < 0 ||
                UncompressedSize < 0 ||
                UncompressedSize > MaximumDecodedBlockBytes ||
                Offset < 0x100 ||
                Offset > archiveLength ||
                DataSize > archiveLength - Offset ||
                !Enum.IsDefined(Property))
            {
                throw new InvalidDataException("RHO block metadata is invalid.");
            }
        }
    }
}
