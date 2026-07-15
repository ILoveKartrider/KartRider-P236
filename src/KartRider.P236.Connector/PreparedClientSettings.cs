using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace KartRider.P236.Connector;

internal sealed record PreparedClientSettingsResult(PreparedPinInfo PinInfo, bool Changed);

internal sealed record RunningClientInspection(
    int ProcessId,
    string? ExecutablePath,
    bool HasExited,
    bool InspectionFailed);

/// <summary>
/// Updates mutable per-instance fields in a validated 2005 PIN image. Original
/// format-v1 images are promoted to the format-v2 representation understood by
/// the client; existing v2 images retain their unknown fields and BML data.
/// KartRider.xml is committed with the PIN and both files are rolled back
/// together if final validation fails.
/// </summary>
internal static class PreparedClientSettings
{
    private const uint PinRtti = 284099454;
    private const int MaximumFileLength = 4 * 1024 * 1024;
    private const int MaximumDecodedLength = 4 * 1024 * 1024;
    private const int MaximumStringCharacters = 16 * 1024;
    private const int MaximumCollectionCount = 1024;
    private const int MaximumBmlDepth = 32;
    internal const string DefaultStorageRoot = "카트라이더_236";
    private const string StoragePrefix = DefaultStorageRoot;
    private const int CommonPinHeaderLength = 15;
    private static readonly Regex ClientDirectoryPattern =
        new Regex("^client[1-9][0-9]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ReservedWindowsNames = new HashSet<string>(
        new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        },
        StringComparer.OrdinalIgnoreCase);

    internal static string GetRecommendedStorageRoot(string rootDirectory, string currentStorageRoot)
    {
        string fullRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string instanceName = Path.GetFileName(fullRoot);
        DirectoryInfo? parent = Directory.GetParent(fullRoot);
        if (parent != null &&
            string.Equals(parent.Name, "clients", StringComparison.OrdinalIgnoreCase) &&
            ClientDirectoryPattern.IsMatch(instanceName))
        {
            string expected = $"{StoragePrefix}_{instanceName.ToLowerInvariant()}";
            string current = NormalizeStorageRoot(currentStorageRoot);
            if (string.Equals(current, StoragePrefix, StringComparison.OrdinalIgnoreCase) ||
                (current.StartsWith(StoragePrefix + "_client", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)))
            {
                // A base/shared root or another clientN root indicates that the
                // instance was copied without preparing its PIN.
                return expected;
            }

            // A deliberate custom folder remains a deliberate custom folder.
            return current;
        }

        return NormalizeStorageRoot(currentStorageRoot);
    }

    internal static IPAddress NormalizeServerAddress(string value)
    {
        string text = (value ?? string.Empty).Trim();
        if (!IPAddress.TryParse(text, out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException(
                "서버 IP에는 IPv4 주소를 입력하세요. 예: 127.0.0.1");
        }

        byte[] bytes = address.GetAddressBytes();
        if (address.Equals(IPAddress.Any) ||
            bytes[0] is >= 224 and <= 239 ||
            address.Equals(IPAddress.Broadcast))
        {
            throw new InvalidOperationException("접속 대상으로 사용할 수 없는 서버 IPv4 주소입니다.");
        }

        return address;
    }

    internal static string NormalizeStorageRoot(string value)
    {
        string storageRoot = (value ?? string.Empty).Trim();
        if (storageRoot.Length == 0 || storageRoot.Length > 120 ||
            Path.IsPathRooted(storageRoot) ||
            storageRoot is "." or ".." ||
            storageRoot.EndsWith('.') ||
            storageRoot.EndsWith(' ') ||
            storageRoot.Contains('/') ||
            storageRoot.Contains('\\') ||
            storageRoot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            ReservedWindowsNames.Contains(storageRoot.Split('.')[0]))
        {
            throw new InvalidOperationException(
                "문서 저장 폴더에는 안전한 단일 하위 폴더 이름을 입력하세요.");
        }

        return storageRoot;
    }

    internal static PreparedClientSettingsResult Apply(
        string rootDirectory,
        string serverAddress,
        ushort loginPort,
        string storageRoot)
    {
        return Apply(
            rootDirectory,
            serverAddress,
            loginPort,
            storageRoot,
            runningClientInspections: null,
            documentsBaseDirectory: null);
    }

    internal static PreparedClientSettingsResult Apply(
        string rootDirectory,
        string serverAddress,
        ushort loginPort,
        string storageRoot,
        IReadOnlyCollection<RunningClientInspection>? runningClientInspections,
        string? documentsBaseDirectory)
    {
        if (loginPort == 0)
        {
            throw new InvalidOperationException("로그인 TCP 포트는 1~65535 범위여야 합니다.");
        }

        string root = Path.GetFullPath(rootDirectory);
        return WithRootMutex(
            root,
            () => ApplyLocked(
                root,
                serverAddress,
                loginPort,
                storageRoot,
                runningClientInspections,
                documentsBaseDirectory));
    }

    internal static void RecoverIfNeeded(string rootDirectory)
    {
        string root = Path.GetFullPath(rootDirectory);
        _ = WithRootMutex(root, () =>
        {
            if (File.Exists(GetTransactionPaths(root).MarkerPath))
            {
                EnsureSelectedClientNotRunning(root, inspections: null);
            }
            RecoverInterruptedUpdateLocked(root);
            return true;
        });
    }

    private static PreparedClientSettingsResult ApplyLocked(
        string root,
        string serverAddress,
        ushort loginPort,
        string storageRoot,
        IReadOnlyCollection<RunningClientInspection>? runningClientInspections,
        string? documentsBaseDirectory)
    {
        if (File.Exists(GetTransactionPaths(root).MarkerPath))
        {
            EnsureSelectedClientNotRunning(root, runningClientInspections);
        }
        RecoverInterruptedUpdateLocked(root);

        string pinPath = Path.Combine(root, "KartRider.pin");
        string configPath = Path.Combine(root, "KartRider.xml");
        IPAddress address = NormalizeServerAddress(serverAddress);
        string normalizedAddress = address.ToString();
        string normalizedStorageRoot = NormalizeStorageRoot(storageRoot);
        string normalizedEndpoint = $"{normalizedAddress}:{loginPort}";

        EnsureUniqueSiblingStorageRoot(root, normalizedStorageRoot);

        PreparedPinInfo before = PreparedPinValidator.InspectConfigurable(root);
        bool needsChange =
            before.Format != 2 ||
            !string.Equals(before.LoginHost, normalizedAddress, StringComparison.OrdinalIgnoreCase) ||
            before.LoginPort != loginPort ||
            before.LoginEndpoints.Any(endpoint =>
                !string.Equals(endpoint, normalizedEndpoint, StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(before.StorageRoot, normalizedStorageRoot, StringComparison.Ordinal);
        if (!needsChange)
        {
            EnsureDocumentDirectories(normalizedStorageRoot, documentsBaseDirectory);
            return new PreparedClientSettingsResult(before, Changed: false);
        }

        EnsureSelectedClientNotRunning(root, runningClientInspections);

        byte[] originalPin = ReadBoundedFile(pinPath);
        byte[] originalConfig = File.ReadAllBytes(configPath);
        byte[] updatedPin = RewritePin(originalPin, address, loginPort, normalizedStorageRoot);
        byte[] updatedConfig = RewriteConfig(originalConfig, normalizedAddress, loginPort);

        // Parse the newly generated PIN before touching either live file.
        PinLayout stagedLayout = ReadPinLayout(updatedPin);
        if (stagedLayout.Format != 2 ||
            stagedLayout.StorageRoot == null ||
            !string.Equals(stagedLayout.StorageRoot.Value, normalizedStorageRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("새 PIN의 문서 저장 폴더 사전 검증에 실패했습니다.");
        }

        TransactionPaths transaction = GetTransactionPaths(root);
        try
        {
            WriteAtomically(transaction.PinBackupPath, originalPin);
            WriteAtomically(transaction.ConfigBackupPath, originalConfig);
            WriteAtomically(
                transaction.MarkerPath,
                Encoding.UTF8.GetBytes($"version=1{Environment.NewLine}created={DateTime.UtcNow:O}{Environment.NewLine}"));

            WriteAtomically(configPath, updatedConfig);
            WriteAtomically(pinPath, updatedPin);

            PreparedPinInfo after = PreparedPinValidator.Validate(root);
            if (!string.Equals(after.LoginHost, normalizedAddress, StringComparison.OrdinalIgnoreCase) ||
                after.LoginPort != loginPort ||
                after.LoginEndpoints.Any(endpoint =>
                    !string.Equals(endpoint, normalizedEndpoint, StringComparison.OrdinalIgnoreCase)) ||
                !string.Equals(after.StorageRoot, normalizedStorageRoot, StringComparison.Ordinal))
            {
                throw new InvalidDataException("저장한 PIN/XML 설정이 요청한 값과 다릅니다.");
            }

            EnsureDocumentDirectories(normalizedStorageRoot, documentsBaseDirectory);
            CompleteTransaction(transaction);
            return new PreparedClientSettingsResult(after, Changed: true);
        }
        catch (Exception updateException)
        {
            try
            {
                if (File.Exists(transaction.MarkerPath))
                {
                    RestoreTransactionLocked(root, transaction);
                }
                else
                {
                    CompleteTransaction(transaction);
                }
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "PIN/XML 설정 저장과 원본 복구가 모두 실패했습니다.",
                    updateException,
                    rollbackException);
            }

            throw new InvalidOperationException(
                "PIN/XML 설정 저장에 실패하여 원래 설정으로 복구했습니다.",
                updateException);
        }
    }

    private static byte[] RewritePin(
        byte[] originalFile,
        IPAddress address,
        ushort loginPort,
        string storageRoot)
    {
        PinLayout layout = ReadPinLayout(originalFile);
        if (layout.Format == 1)
        {
            byte[] upgraded = BuildUpgradedV2Pin(layout, address, loginPort, storageRoot);
            return EncodeFile(upgraded, layout.EncodingFlags, layout.CryptoKey);
        }

        if (layout.Format != 2 || layout.StorageRoot == null)
        {
            throw new InvalidDataException($"지원하지 않는 2005 PIN 형식입니다 (format={layout.Format}).");
        }

        StringSlot rootSlot = layout.StorageRoot;
        byte[] decoded = layout.Decoded.ToArray();
        byte[] addressBytes = address.GetAddressBytes();
        foreach (int endpointOffset in layout.EndpointOffsets)
        {
            addressBytes.CopyTo(decoded, endpointOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(endpointOffset + 4, 2), loginPort);
        }

        byte[] rootBytes = Encoding.Unicode.GetBytes(storageRoot);
        int oldFieldLength = checked(4 + rootSlot.CharacterCount * 2);
        int newFieldLength = checked(4 + rootBytes.Length);
        byte[] rewritten = new byte[checked(decoded.Length - oldFieldLength + newFieldLength)];
        int prefixOffset = rootSlot.LengthPrefixOffset;
        decoded.AsSpan(0, prefixOffset).CopyTo(rewritten);
        BinaryPrimitives.WriteInt32LittleEndian(
            rewritten.AsSpan(prefixOffset, 4),
            storageRoot.Length);
        rootBytes.CopyTo(rewritten, prefixOffset + 4);
        decoded.AsSpan(prefixOffset + oldFieldLength)
            .CopyTo(rewritten.AsSpan(prefixOffset + newFieldLength));

        return EncodeFile(rewritten, layout.EncodingFlags, layout.CryptoKey);
    }

    private static byte[] BuildUpgradedV2Pin(
        PinLayout layout,
        IPAddress address,
        ushort loginPort,
        string storageRoot)
    {
        if (layout.Format != 1 || layout.LegacyUrl == null ||
            layout.Decoded.Length < CommonPinHeaderLength)
        {
            throw new InvalidDataException("원본 format-v1 PIN 구조가 올바르지 않습니다.");
        }

        using MemoryStream output = new MemoryStream();
        byte[] baseHeader = layout.Decoded.AsSpan(0, CommonPinHeaderLength).ToArray();
        baseHeader[4] = 2;
        output.Write(baseHeader, 0, baseHeader.Length);

        output.WriteByte(2); // login type
        WriteString(output, string.Empty); // AES key
        WriteString(output, layout.LegacyUrl); // preserve the original service URL
        WriteString(output, string.Empty); // patch URL

        WriteInt32(output, 1); // auth method count
        output.WriteByte(1);
        WriteString(output, "Default");
        output.WriteByte(0); // account BML
        WriteInt32(output, 1); // one authoritative login endpoint
        byte[] addressBytes = address.GetAddressBytes();
        output.Write(addressBytes, 0, addressBytes.Length);
        WriteUInt16(output, loginPort);
        output.WriteByte(0); // auth extra BML

        output.WriteByte(1); // storage BML
        WriteString(output, "storage");
        WriteString(output, string.Empty);
        WriteInt32(output, 0); // storage values
        WriteInt32(output, 1); // storage children

        WriteString(output, "document");
        WriteString(output, string.Empty);
        WriteInt32(output, 3);
        WriteString(output, "root");
        WriteString(output, storageRoot);
        WriteString(output, "screenShot");
        WriteString(output, "스크린샷");
        WriteString(output, "riderData");
        WriteString(output, "라이더데이터");
        WriteInt32(output, 0); // document children
        output.WriteByte(0); // top-level extra BML
        return output.ToArray();
    }

    private static PinLayout ReadPinLayout(byte[] file)
    {
        if (file.Length <= 4 || file.Length > MaximumFileLength)
        {
            throw new InvalidDataException("KartRider.pin 크기가 허용 범위를 벗어났습니다.");
        }

        PacketCursor envelope = new PacketCursor(file);
        int encodedLength = envelope.ReadInt32();
        if (encodedLength <= 0 || encodedLength != envelope.Remaining)
        {
            throw new InvalidDataException("KartRider.pin 봉투 길이가 올바르지 않습니다.");
        }

        byte[] encoded = envelope.ReadBytes(encodedLength);
        PacketCursor encodedCursor = new PacketCursor(encoded);
        if (encodedCursor.ReadByte() != 0x53)
        {
            throw new InvalidDataException("KartRider.pin encoded block magic이 올바르지 않습니다.");
        }

        byte flags = encodedCursor.ReadByte();
        if ((flags & ~3) != 0)
        {
            throw new InvalidDataException($"지원하지 않는 PIN encoding flag입니다: 0x{flags:X2}");
        }

        uint expectedChecksum = encodedCursor.ReadUInt32();
        uint cryptoKey = (flags & 2) != 0 ? encodedCursor.ReadUInt32() : 0;
        int expectedLength = (flags & 1) != 0 ? encodedCursor.ReadInt32() : encodedCursor.Remaining;
        if (expectedLength < 0 || expectedLength > MaximumDecodedLength)
        {
            throw new InvalidDataException("KartRider.pin decoded 길이가 허용 범위를 벗어났습니다.");
        }

        byte[] payload = encodedCursor.ReadBytes(encodedCursor.Remaining);
        if ((flags & 2) != 0)
        {
            ApplyKartCrypto(payload, cryptoKey);
        }

        byte[] decoded;
        if ((flags & 1) != 0)
        {
            using MemoryStream input = new MemoryStream(payload, writable: false);
            using ZLibStream zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: false);
            using MemoryStream output = new MemoryStream(Math.Min(expectedLength, MaximumDecodedLength));
            byte[] buffer = new byte[8192];
            while (true)
            {
                int read = zlib.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }
                if (output.Length + read > MaximumDecodedLength)
                {
                    throw new InvalidDataException("KartRider.pin 압축 해제 결과가 너무 큽니다.");
                }
                output.Write(buffer, 0, read);
            }
            decoded = output.ToArray();
        }
        else
        {
            decoded = payload;
        }

        if (decoded.Length != expectedLength || GenerateKartAdler32(decoded) != expectedChecksum)
        {
            throw new InvalidDataException("KartRider.pin decoded 길이 또는 checksum이 올바르지 않습니다.");
        }

        PacketCursor pin = new PacketCursor(decoded);
        if (pin.ReadUInt32() != PinRtti)
        {
            throw new InvalidDataException("KartRider.pin의 PinObject 식별자가 올바르지 않습니다.");
        }

        byte format = pin.ReadByte();
        ushort localeId = pin.ReadUInt16();
        ushort clientLocation = pin.ReadUInt16();
        _ = pin.ReadByte();
        _ = pin.ReadByte();
        ushort minorVersion = pin.ReadUInt16();
        _ = pin.ReadByte();
        _ = pin.ReadByte();
        if (localeId != 1002 || clientLocation != 118 || minorVersion != 236)
        {
            throw new InvalidDataException(
                $"지원하지 않는 PIN 헤더입니다 (locale={localeId}, location={clientLocation}, version={minorVersion}).");
        }

        if (format == 1)
        {
            string legacyUrl = pin.ReadString();
            int endpointCount = pin.ReadCount("로그인 서버");
            if (endpointCount == 0)
            {
                throw new InvalidDataException("KartRider.pin에 로그인 서버가 없습니다.");
            }
            for (int endpointIndex = 0; endpointIndex < endpointCount; endpointIndex++)
            {
                _ = pin.ReadBytes(6);
            }
            if (pin.Remaining != 0)
            {
                throw new InvalidDataException("format-v1 KartRider.pin 끝에 해석되지 않은 데이터가 있습니다.");
            }

            return new PinLayout(
                format,
                decoded,
                flags,
                cryptoKey,
                Array.Empty<int>(),
                null,
                legacyUrl);
        }

        if (format != 2)
        {
            throw new InvalidDataException($"지원하지 않는 2005 PIN 형식입니다 (format={format}).");
        }

        _ = pin.ReadByte();
        _ = pin.ReadString();
        _ = pin.ReadString();
        _ = pin.ReadString();

        List<int> endpointOffsets = new List<int>();
        int authCount = pin.ReadCount("인증 방식");
        for (int authIndex = 0; authIndex < authCount; authIndex++)
        {
            _ = pin.ReadByte();
            _ = pin.ReadString();
            _ = ReadOptionalBml(pin, 0);
            int endpointCount = pin.ReadCount("로그인 서버");
            for (int endpointIndex = 0; endpointIndex < endpointCount; endpointIndex++)
            {
                endpointOffsets.Add(pin.Position);
                _ = pin.ReadBytes(6);
            }
            _ = ReadOptionalBml(pin, 0);
        }

        if (endpointOffsets.Count == 0)
        {
            throw new InvalidDataException("KartRider.pin에 편집할 로그인 서버가 없습니다.");
        }

        BmlNode? storage = pin.Remaining > 0 ? ReadOptionalBml(pin, 0) : null;
        _ = pin.Remaining > 0 ? ReadOptionalBml(pin, 0) : null;
        if (pin.Remaining != 0 ||
            storage == null ||
            !string.Equals(storage.Name, "storage", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("KartRider.pin storage 구조가 올바르지 않습니다.");
        }

        BmlNode? document = storage.Children.FirstOrDefault(child =>
            string.Equals(child.Name, "document", StringComparison.OrdinalIgnoreCase));
        if (document == null || !document.Values.TryGetValue("root", out StringSlot? rootSlot))
        {
            throw new InvalidDataException("KartRider.pin storage/document root를 찾지 못했습니다.");
        }

        return new PinLayout(
            format,
            decoded,
            flags,
            cryptoKey,
            endpointOffsets,
            rootSlot,
            null);
    }

    private static byte[] EncodeFile(byte[] decoded, byte flags, uint cryptoKey)
    {
        byte[] payload;
        if ((flags & 1) != 0)
        {
            using MemoryStream compressed = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(
                       compressed,
                       CompressionLevel.Optimal,
                       leaveOpen: true))
            {
                zlib.Write(decoded, 0, decoded.Length);
            }
            payload = compressed.ToArray();
        }
        else
        {
            payload = decoded.ToArray();
        }

        if ((flags & 2) != 0)
        {
            ApplyKartCrypto(payload, cryptoKey);
        }

        int encodedHeaderLength = 6 + ((flags & 2) != 0 ? 4 : 0) + ((flags & 1) != 0 ? 4 : 0);
        byte[] encoded = new byte[checked(encodedHeaderLength + payload.Length)];
        int offset = 0;
        encoded[offset++] = 0x53;
        encoded[offset++] = flags;
        BinaryPrimitives.WriteUInt32LittleEndian(
            encoded.AsSpan(offset, 4),
            GenerateKartAdler32(decoded));
        offset += 4;
        if ((flags & 2) != 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(offset, 4), cryptoKey);
            offset += 4;
        }
        if ((flags & 1) != 0)
        {
            BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(offset, 4), decoded.Length);
            offset += 4;
        }
        payload.CopyTo(encoded, offset);

        byte[] file = new byte[checked(4 + encoded.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0, 4), encoded.Length);
        encoded.CopyTo(file, 4);
        return file;
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteString(Stream stream, string value)
    {
        if (value.Length > MaximumStringCharacters)
        {
            throw new InvalidDataException(
                $"KartRider.pin 문자열 길이가 허용 범위를 벗어났습니다: {value.Length}");
        }

        WriteInt32(stream, value.Length);
        byte[] bytes = Encoding.Unicode.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] RewriteConfig(byte[] original, string address, ushort loginPort)
    {
        XDocument document = LegacyXml.Load(original, LoadOptions.PreserveWhitespace);
        if (document.Root == null ||
            !string.Equals(document.Root.Name.LocalName, "config", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("KartRider.xml의 루트가 config가 아닙니다.");
        }

        XElement? server = document.Root.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "server", StringComparison.OrdinalIgnoreCase));
        if (server == null)
        {
            server = new XElement("server");
            document.Root.AddFirst(server);
        }
        server.SetAttributeValue("addr", $"{address}:{loginPort}");
        document.Declaration = new XDeclaration("1.0", "utf-16", null);

        using MemoryStream output = new MemoryStream();
        XmlWriterSettings writerSettings = new XmlWriterSettings
        {
            Encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false
        };
        using (XmlWriter writer = XmlWriter.Create(output, writerSettings))
        {
            document.Save(writer);
        }
        return output.ToArray();
    }

    private static void EnsureDocumentDirectories(
        string storageRoot,
        string? documentsBaseDirectory)
    {
        string documents = documentsBaseDirectory == null
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : Path.GetFullPath(documentsBaseDirectory);
        string root = Path.Combine(documents, storageRoot);
        Directory.CreateDirectory(Path.Combine(root, "스크린샷"));
        Directory.CreateDirectory(Path.Combine(root, "라이더데이터"));
    }

    private static void EnsureSelectedClientNotRunning(
        string rootDirectory,
        IReadOnlyCollection<RunningClientInspection>? inspections)
    {
        string selectedExecutable = Path.GetFullPath(Path.Combine(rootDirectory, "KartRider.exe"));
        EnsureNoRunningClientMutation(
            selectedExecutable,
            inspections ?? InspectRunningClients());
    }

    internal static void EnsureNoRunningClientMutation(
        string selectedExecutable,
        IEnumerable<RunningClientInspection> inspections)
    {
        string selectedPath = Path.GetFullPath(selectedExecutable);
        foreach (RunningClientInspection inspection in inspections)
        {
            if (inspection.HasExited)
            {
                continue;
            }

            if (inspection.InspectionFailed || string.IsNullOrWhiteSpace(inspection.ExecutablePath))
            {
                throw new InvalidOperationException(
                    $"실행 중인 KartRider 프로세스(PID {inspection.ProcessId})의 경로를 안전하게 확인할 수 없습니다. " +
                    "모든 KartRider를 종료하거나 게임과 동일한 권한으로 접속기를 다시 실행하세요.");
            }

            string runningPath;
            try
            {
                runningPath = Path.GetFullPath(inspection.ExecutablePath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidOperationException(
                    $"실행 중인 KartRider 프로세스(PID {inspection.ProcessId})의 경로를 안전하게 확인할 수 없습니다. " +
                    "모든 KartRider를 종료하거나 게임과 동일한 권한으로 접속기를 다시 실행하세요.",
                    exception);
            }

            if (string.Equals(runningPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "선택한 클라이언트가 실행 중이라 PIN/XML 설정을 변경할 수 없습니다.");
            }
        }
    }

    private static IReadOnlyList<RunningClientInspection> InspectRunningClients()
    {
        List<RunningClientInspection> inspections = new List<RunningClientInspection>();
        foreach (Process process in Process.GetProcessesByName("KartRider"))
        {
            using (process)
            {
                int processId = process.Id;
                try
                {
                    string? runningExecutable = process.MainModule?.FileName;
                    inspections.Add(new RunningClientInspection(
                        processId,
                        runningExecutable,
                        HasExited: process.HasExited,
                        InspectionFailed: false));
                }
                catch (Win32Exception)
                {
                    bool hasExited = HasExitedSafely(process);
                    inspections.Add(new RunningClientInspection(
                        processId,
                        ExecutablePath: null,
                        HasExited: hasExited,
                        InspectionFailed: !hasExited));
                }
                catch (InvalidOperationException)
                {
                    bool hasExited = HasExitedSafely(process);
                    inspections.Add(new RunningClientInspection(
                        processId,
                        ExecutablePath: null,
                        HasExited: hasExited,
                        InspectionFailed: !hasExited));
                }
            }
        }

        return inspections;
    }

    private static bool HasExitedSafely(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static byte[] ReadBoundedFile(string path)
    {
        FileInfo info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("준비된 인스턴스에 KartRider.pin이 없습니다.", path);
        }
        if (info.Length <= 4 || info.Length > MaximumFileLength)
        {
            throw new InvalidDataException("KartRider.pin 크기가 허용 범위를 벗어났습니다.");
        }
        return File.ReadAllBytes(path);
    }

    private static T WithRootMutex<T>(string rootDirectory, Func<T> action)
    {
        string fullRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        DirectoryInfo? parent = Directory.GetParent(fullRoot);
        string lockScope = parent != null &&
            string.Equals(parent.Name, "clients", StringComparison.OrdinalIgnoreCase)
                ? parent.FullName.ToUpperInvariant()
                : fullRoot;
        string identity = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(lockScope)));
        using Mutex mutex = new Mutex(
            initiallyOwned: false,
            name: $@"Local\KartRider2005Launcher.Settings.{identity}");
        bool ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                throw new TimeoutException("다른 접속기가 이 클라이언트의 PIN/XML 설정을 저장 중입니다.");
            }

            return action();
        }
        finally
        {
            if (ownsMutex)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static void EnsureUniqueSiblingStorageRoot(
        string selectedRootDirectory,
        string selectedStorageRoot)
    {
        string selectedRoot = Path.GetFullPath(selectedRootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DirectoryInfo? parent = Directory.GetParent(selectedRoot);
        if (parent == null ||
            !string.Equals(parent.Name, "clients", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string siblingDirectory in Directory.EnumerateDirectories(
                     parent.FullName,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            string siblingRoot = Path.GetFullPath(siblingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(siblingRoot, selectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string siblingExecutable = Path.Combine(siblingRoot, "KartRider.exe");
            if (!File.Exists(siblingExecutable) ||
                !OriginalClientValidator.IsKnownOriginal(siblingExecutable))
            {
                continue;
            }

            try
            {
                RecoverInterruptedUpdateLocked(siblingRoot);
                PreparedPinInfo siblingPin = PreparedPinValidator.Validate(siblingRoot);
                if (string.Equals(
                        siblingPin.StorageRoot,
                        selectedStorageRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"문서 저장 폴더 '{selectedStorageRoot}'는 이미 " +
                        $"{Path.GetFileName(siblingRoot)}에서 사용 중입니다.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
            {
                // Invalid/unprepared sibling directories are not launchable and
                // do not participate in the storage-root uniqueness invariant.
            }
        }
    }

    private static TransactionPaths GetTransactionPaths(string rootDirectory) =>
        new TransactionPaths(
            Path.Combine(rootDirectory, "KartRider.launcher-settings.transaction"),
            Path.Combine(rootDirectory, "KartRider.pin.launcher-settings.bak"),
            Path.Combine(rootDirectory, "KartRider.xml.launcher-settings.bak"));

    private static void RecoverInterruptedUpdateLocked(string rootDirectory)
    {
        TransactionPaths transaction = GetTransactionPaths(rootDirectory);
        if (!File.Exists(transaction.MarkerPath))
        {
            // Backups can remain if the process stopped before publishing the
            // marker. No live file is changed before the marker is durable.
            try
            {
                // A pristine format-v1 source may legitimately have different
                // PIN and XML endpoints. With no marker, only verify that the
                // live source is configurable before deleting orphan backups.
                _ = PreparedPinValidator.InspectConfigurable(rootDirectory);
                CompleteTransaction(transaction);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
            {
                // Preserve orphan backups when the live pair is independently
                // invalid; normal validation will report the underlying issue.
            }
            return;
        }

        try
        {
            // A valid live pair means either both replacements completed or no
            // replacement started. In both cases it is safe to finalize it.
            _ = PreparedPinValidator.InspectConfigurablePair(rootDirectory);
            CompleteTransaction(transaction);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            RestoreTransactionLocked(rootDirectory, transaction);
        }
    }

    private static void RestoreTransactionLocked(
        string rootDirectory,
        TransactionPaths transaction)
    {
        if (!File.Exists(transaction.PinBackupPath) || !File.Exists(transaction.ConfigBackupPath))
        {
            throw new InvalidDataException("미완료 PIN/XML 설정의 복구 백업이 없습니다.");
        }

        byte[] pin = ReadBoundedFile(transaction.PinBackupPath);
        byte[] config = File.ReadAllBytes(transaction.ConfigBackupPath);
        WriteAtomically(Path.Combine(rootDirectory, "KartRider.xml"), config);
        WriteAtomically(Path.Combine(rootDirectory, "KartRider.pin"), pin);
        // Backups can contain the original format-v1 mismatch that the first
        // preparation is specifically meant to repair. The strict live-pair
        // check above already decided that rollback was required.
        _ = PreparedPinValidator.InspectConfigurable(rootDirectory);
        CompleteTransaction(transaction);
    }

    private static void CompleteTransaction(TransactionPaths transaction)
    {
        // Remove the marker first. Remaining backups are harmless orphans and
        // will be cleaned on the next successful validation if deletion fails.
        bool markerRemoved = TryDelete(transaction.MarkerPath);
        if (markerRemoved || !File.Exists(transaction.MarkerPath))
        {
            _ = TryDelete(transaction.PinBackupPath);
            _ = TryDelete(transaction.ConfigBackupPath);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            // Recovery/cleanup will retry on the next operation.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Recovery/cleanup will retry on the next operation.
            return false;
        }
    }

    private static void WriteAtomically(string path, byte[] data)
    {
        string temporaryPath = path + ".launcher-settings.tmp";
        try
        {
            using (FileStream stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static BmlNode? ReadOptionalBml(PacketCursor cursor, int depth)
    {
        byte present = cursor.ReadByte();
        return present switch
        {
            0 => null,
            1 => ReadBml(cursor, depth),
            _ => throw new InvalidDataException("KartRider.pin BML present 값이 0/1이 아닙니다.")
        };
    }

    private static BmlNode ReadBml(PacketCursor cursor, int depth)
    {
        if (depth >= MaximumBmlDepth)
        {
            throw new InvalidDataException("KartRider.pin BML 중첩이 너무 깊습니다.");
        }

        BmlNode node = new BmlNode(cursor.ReadString(), cursor.ReadString());
        int valueCount = cursor.ReadCount("BML 값");
        for (int index = 0; index < valueCount; index++)
        {
            string key = cursor.ReadString();
            StringSlot value = cursor.ReadStringSlot();
            if (!node.Values.TryAdd(key, value))
            {
                throw new InvalidDataException($"KartRider.pin BML에 중복 키가 있습니다: {key}");
            }
        }

        int childCount = cursor.ReadCount("BML 하위 객체");
        for (int index = 0; index < childCount; index++)
        {
            node.Children.Add(ReadBml(cursor, depth + 1));
        }
        return node;
    }

    private static void ApplyKartCrypto(byte[] bytes, uint key)
    {
        uint[] state = new uint[17];
        state[0] = key ^ 2222193601U;
        for (int index = 1; index < 16; index++)
        {
            state[index] = unchecked(state[index - 1] - 2072773695U);
        }

        byte[] stateBytes = new byte[68];
        for (int index = 0; index <= 16; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(stateBytes.AsSpan(index * 4, 4), state[index]);
        }

        int offset = 0;
        for (; offset + 64 <= bytes.Length; offset += 64)
        {
            for (int word = 0; word < 16; word++)
            {
                int wordOffset = offset + word * 4;
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(wordOffset, 4));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(wordOffset, 4),
                    value ^ state[word]);
            }
        }

        for (int index = offset; index < bytes.Length; index++)
        {
            bytes[index] ^= stateBytes[index - offset];
        }
    }

    private static uint GenerateKartAdler32(byte[] bytes)
    {
        const uint modulus = 65521;
        uint a = 0;
        uint b = 0;
        foreach (byte value in bytes)
        {
            a += value;
            if (a >= modulus)
            {
                a -= modulus;
            }
            b = (b + a) % modulus;
        }
        return a | (b << 16);
    }

    private sealed class PacketCursor
    {
        private readonly byte[] _data;
        private int _position;

        internal PacketCursor(byte[] data) => _data = data;
        internal int Position => _position;
        internal int Remaining => _data.Length - _position;

        internal byte ReadByte()
        {
            Ensure(1);
            return _data[_position++];
        }

        internal ushort ReadUInt16()
        {
            Ensure(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position, 2));
            _position += 2;
            return value;
        }

        internal uint ReadUInt32()
        {
            Ensure(4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_position, 4));
            _position += 4;
            return value;
        }

        internal int ReadInt32()
        {
            Ensure(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_position, 4));
            _position += 4;
            return value;
        }

        internal int ReadCount(string field)
        {
            int count = ReadInt32();
            if (count < 0 || count > MaximumCollectionCount)
            {
                throw new InvalidDataException($"KartRider.pin {field} 개수가 허용 범위를 벗어났습니다: {count}");
            }
            return count;
        }

        internal string ReadString() => ReadStringSlot().Value;

        internal StringSlot ReadStringSlot()
        {
            int prefixOffset = _position;
            int characterCount = ReadInt32();
            if (characterCount < 0 || characterCount > MaximumStringCharacters)
            {
                throw new InvalidDataException(
                    $"KartRider.pin 문자열 길이가 허용 범위를 벗어났습니다: {characterCount}");
            }

            int byteCount = checked(characterCount * 2);
            Ensure(byteCount);
            string value = Encoding.Unicode.GetString(_data, _position, byteCount);
            _position += byteCount;
            return new StringSlot(value, prefixOffset, characterCount);
        }

        internal byte[] ReadBytes(int count)
        {
            Ensure(count);
            byte[] value = _data.AsSpan(_position, count).ToArray();
            _position += count;
            return value;
        }

        private void Ensure(int count)
        {
            if (count < 0 || count > Remaining)
            {
                throw new InvalidDataException("KartRider.pin 데이터가 예상보다 짧습니다.");
            }
        }
    }

    private sealed record StringSlot(string Value, int LengthPrefixOffset, int CharacterCount);

    private sealed class BmlNode
    {
        internal BmlNode(string name, string value)
        {
            Name = name;
            Value = value;
        }

        internal string Name { get; }
        internal string Value { get; }
        internal Dictionary<string, StringSlot> Values { get; } =
            new Dictionary<string, StringSlot>(StringComparer.OrdinalIgnoreCase);
        internal List<BmlNode> Children { get; } = new List<BmlNode>();
    }

    private sealed record PinLayout(
        byte Format,
        byte[] Decoded,
        byte EncodingFlags,
        uint CryptoKey,
        IReadOnlyList<int> EndpointOffsets,
        StringSlot? StorageRoot,
        string? LegacyUrl);

    private sealed record TransactionPaths(
        string MarkerPath,
        string PinBackupPath,
        string ConfigBackupPath);
}
