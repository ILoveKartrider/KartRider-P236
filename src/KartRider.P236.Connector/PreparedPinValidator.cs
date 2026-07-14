using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace KartRider.P236.Connector;

internal sealed record PreparedPinInfo(
    byte Format,
    ushort LocaleId,
    ushort ClientLocation,
    ushort MinorVersion,
    string LoginHost,
    ushort LoginPort,
    IReadOnlyList<string> LoginEndpoints,
    string StorageRoot)
{
    internal string LoginEndpoint => $"{LoginHost}:{LoginPort}";
}

/// <summary>
/// Performs bounded, read-only validation of a legacy PIN image. Configuration
/// flows may inspect an original v1 image; launch validation still requires the
/// prepared v2 representation.
/// </summary>
internal static class PreparedPinValidator
{
    private const uint PinRtti = 284099454;
    private const int MaximumFileLength = 4 * 1024 * 1024;
    private const int MaximumDecodedLength = 4 * 1024 * 1024;
    private const int MaximumStringCharacters = 16 * 1024;
    private const int MaximumCollectionCount = 1024;
    private const int MaximumBmlDepth = 32;

    internal static PreparedPinInfo Validate(string rootDirectory) =>
        ValidateCore(rootDirectory, requirePreparedV2: true);

    internal static PreparedPinInfo InspectConfigurable(string rootDirectory) =>
        ValidateCore(rootDirectory, requirePreparedV2: false);

    private static PreparedPinInfo ValidateCore(
        string rootDirectory,
        bool requirePreparedV2)
    {
        string pinPath = Path.Combine(rootDirectory, "KartRider.pin");
        string configPath = Path.Combine(rootDirectory, "KartRider.xml");
        byte[] file = ReadBoundedFile(pinPath);
        PacketCursor envelope = new PacketCursor(file);
        int encodedLength = envelope.ReadInt32();
        if (encodedLength <= 0 || encodedLength != envelope.Remaining)
        {
            throw new InvalidDataException("KartRider.pin 봉투 길이가 올바르지 않습니다.");
        }

        byte[] decoded = DecodeBlock(envelope.ReadBytes(encodedLength));
        PacketCursor pin = new PacketCursor(decoded);
        if (pin.ReadUInt32() != PinRtti)
        {
            throw new InvalidDataException("KartRider.pin의 PinObject 식별자가 올바르지 않습니다.");
        }

        byte format = pin.ReadByte();
        ushort localeId = pin.ReadUInt16();
        ushort clientLocation = pin.ReadUInt16();
        _ = pin.ReadByte(); // locale type
        _ = pin.ReadByte();
        ushort minorVersion = pin.ReadUInt16();
        _ = pin.ReadByte();
        _ = pin.ReadByte();

        if (localeId != 1002 || clientLocation != 118 || minorVersion != 236)
        {
            throw new InvalidDataException(
                $"지원하지 않는 PIN 헤더입니다 (locale={localeId}, location={clientLocation}, version={minorVersion}). " +
                "필요한 값은 1002/118/236입니다.");
        }

        List<EndpointValue> endpoints = new List<EndpointValue>();
        string storageRoot;
        if (format == 1)
        {
            if (requirePreparedV2)
            {
                throw new InvalidDataException(
                    "원본 format-v1 PIN입니다. 접속기에서 서버 설정을 저장해 v2로 준비하세요.");
            }

            _ = pin.ReadString(); // original URL
            int endpointCount = pin.ReadCount("로그인 서버");
            for (int endpointIndex = 0; endpointIndex < endpointCount; endpointIndex++)
            {
                byte a = pin.ReadByte();
                byte b = pin.ReadByte();
                byte c = pin.ReadByte();
                byte d = pin.ReadByte();
                ushort port = pin.ReadUInt16();
                endpoints.Add(new EndpointValue($"{a}.{b}.{c}.{d}", port));
            }
            storageRoot = PreparedClientSettings.DefaultStorageRoot;
        }
        else if (format == 2)
        {
            _ = pin.ReadByte(); // login type
            _ = pin.ReadString(); // AES key
            _ = pin.ReadString(); // URL
            _ = pin.ReadString(); // patch URL

            int authCount = pin.ReadCount("인증 방식");
            for (int authIndex = 0; authIndex < authCount; authIndex++)
            {
                _ = pin.ReadByte();
                _ = pin.ReadString();
                _ = ReadOptionalBml(pin, 0);

                int endpointCount = pin.ReadCount("로그인 서버");
                for (int endpointIndex = 0; endpointIndex < endpointCount; endpointIndex++)
                {
                    byte a = pin.ReadByte();
                    byte b = pin.ReadByte();
                    byte c = pin.ReadByte();
                    byte d = pin.ReadByte();
                    ushort port = pin.ReadUInt16();
                    endpoints.Add(new EndpointValue($"{a}.{b}.{c}.{d}", port));
                }

                _ = ReadOptionalBml(pin, 0);
            }

            BmlValue? storage = pin.Remaining > 0 ? ReadOptionalBml(pin, 0) : null;
            _ = pin.Remaining > 0 ? ReadOptionalBml(pin, 0) : null;
            if (storage == null || !string.Equals(storage.Name, "storage", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("KartRider.pin에 인스턴스별 storage 설정이 없습니다.");
            }

            BmlValue? document = storage.Children.FirstOrDefault(child =>
                string.Equals(child.Name, "document", StringComparison.OrdinalIgnoreCase));
            if (document == null ||
                !document.Values.TryGetValue("root", out string? configuredStorageRoot) ||
                string.IsNullOrWhiteSpace(configuredStorageRoot))
            {
                throw new InvalidDataException("KartRider.pin의 storage/document root가 비어 있습니다.");
            }

            storageRoot = configuredStorageRoot.Trim();
            ValidateStorageRoot(storageRoot);
        }
        else
        {
            throw new InvalidDataException($"지원하지 않는 2005 PIN 형식입니다 (format={format}).");
        }

        if (pin.Remaining != 0)
        {
            throw new InvalidDataException("KartRider.pin 끝에 해석되지 않은 데이터가 있습니다.");
        }

        if (endpoints.Count == 0)
        {
            throw new InvalidDataException("KartRider.pin에 로그인 서버가 없습니다.");
        }

        EndpointValue configuredEndpoint = ReadConfiguredEndpoint(configPath);
        EndpointValue? matchingEndpoint = endpoints.FirstOrDefault(endpoint =>
            endpoint.Port == configuredEndpoint.Port && HostsEqual(endpoint.Host, configuredEndpoint.Host));
        if (matchingEndpoint == null)
        {
            throw new InvalidDataException(
                $"PIN 로그인 서버와 KartRider.xml 서버가 다릅니다 " +
                $"(PIN={string.Join(", ", endpoints)}, XML={configuredEndpoint}).");
        }

        return new PreparedPinInfo(
            format,
            localeId,
            clientLocation,
            minorVersion,
            matchingEndpoint.Host,
            matchingEndpoint.Port,
            endpoints.Select(endpoint => endpoint.ToString()).ToArray(),
            storageRoot);
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
            throw new InvalidDataException($"KartRider.pin 크기가 허용 범위를 벗어났습니다: {info.Length:N0} bytes");
        }

        return File.ReadAllBytes(path);
    }

    private static byte[] DecodeBlock(byte[] encoded)
    {
        PacketCursor cursor = new PacketCursor(encoded);
        if (cursor.ReadByte() != 0x53)
        {
            throw new InvalidDataException("KartRider.pin encoded block magic이 올바르지 않습니다.");
        }

        byte flags = cursor.ReadByte();
        if ((flags & ~3) != 0)
        {
            throw new InvalidDataException($"지원하지 않는 PIN encoding flag입니다: 0x{flags:X2}");
        }

        uint expectedChecksum = cursor.ReadUInt32();
        uint cryptoKey = (flags & 2) != 0 ? cursor.ReadUInt32() : 0;
        int expectedLength = (flags & 1) != 0 ? cursor.ReadInt32() : cursor.Remaining;
        if (expectedLength < 0 || expectedLength > MaximumDecodedLength)
        {
            throw new InvalidDataException("KartRider.pin decoded 길이가 허용 범위를 벗어났습니다.");
        }

        byte[] payload = cursor.ReadBytes(cursor.Remaining);
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

        if (decoded.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"KartRider.pin decoded 길이가 다릅니다 (expected={expectedLength}, actual={decoded.Length}).");
        }

        if (GenerateKartAdler32(decoded) != expectedChecksum)
        {
            throw new InvalidDataException("KartRider.pin decoded checksum이 올바르지 않습니다.");
        }

        return decoded;
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

    private static BmlValue? ReadOptionalBml(PacketCursor cursor, int depth)
    {
        byte present = cursor.ReadByte();
        if (present == 0)
        {
            return null;
        }
        if (present != 1)
        {
            throw new InvalidDataException("KartRider.pin BML present 값이 0/1이 아닙니다.");
        }
        return ReadBml(cursor, depth);
    }

    private static BmlValue ReadBml(PacketCursor cursor, int depth)
    {
        if (depth >= MaximumBmlDepth)
        {
            throw new InvalidDataException("KartRider.pin BML 중첩이 너무 깊습니다.");
        }

        BmlValue result = new BmlValue(cursor.ReadString(), cursor.ReadString());
        int valueCount = cursor.ReadCount("BML 값");
        for (int index = 0; index < valueCount; index++)
        {
            string key = cursor.ReadString();
            string value = cursor.ReadString();
            if (!result.Values.TryAdd(key, value))
            {
                throw new InvalidDataException($"KartRider.pin BML에 중복 키가 있습니다: {key}");
            }
        }

        int childCount = cursor.ReadCount("BML 하위 객체");
        for (int index = 0; index < childCount; index++)
        {
            result.Children.Add(ReadBml(cursor, depth + 1));
        }
        return result;
    }

    private static EndpointValue ReadConfiguredEndpoint(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("준비된 인스턴스에 KartRider.xml이 없습니다.", configPath);
        }

        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using FileStream stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using XmlReader reader = XmlReader.Create(stream, settings);
        XDocument document = XDocument.Load(reader);
        XElement? server = document.Descendants().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "server", StringComparison.OrdinalIgnoreCase));
        string? address = server?.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, "addr", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidDataException("KartRider.xml에 config/server addr가 없습니다.");
        }

        return EndpointValue.Parse(address);
    }

    private static void ValidateStorageRoot(string storageRoot)
    {
        if (Path.IsPathRooted(storageRoot) ||
            storageRoot is "." or ".." ||
            storageRoot.Contains('/') ||
            storageRoot.Contains('\\') ||
            storageRoot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException(
                $"PIN storage root가 안전한 문서 하위 폴더 이름이 아닙니다: {storageRoot}");
        }
    }

    private static bool HostsEqual(string left, string right)
    {
        static string Normalize(string value) =>
            string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)
                ? IPAddress.Loopback.ToString()
                : value.Trim('[', ']');

        left = Normalize(left);
        right = Normalize(right);
        if (IPAddress.TryParse(left, out IPAddress? leftAddress) &&
            IPAddress.TryParse(right, out IPAddress? rightAddress))
        {
            return leftAddress.Equals(rightAddress);
        }
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PacketCursor
    {
        private readonly byte[] _data;
        private int _position;

        internal PacketCursor(byte[] data) => _data = data;
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

        internal string ReadString()
        {
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
            return value;
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

    private sealed class BmlValue
    {
        internal BmlValue(string name, string value)
        {
            Name = name;
            Value = value;
        }

        internal string Name { get; }
        internal string Value { get; }
        internal Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal List<BmlValue> Children { get; } = new();
    }

    private sealed record EndpointValue(string Host, ushort Port)
    {
        internal static EndpointValue Parse(string value)
        {
            string text = value.Trim();
            if (!Uri.TryCreate("tcp://" + text, UriKind.Absolute, out Uri? uri) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                uri.Port <= 0 || uri.Port > ushort.MaxValue)
            {
                throw new InvalidDataException($"KartRider.xml 서버 주소가 올바르지 않습니다: {value}");
            }
            return new EndpointValue(uri.Host, (ushort)uri.Port);
        }

        public override string ToString() => $"{Host}:{Port}";
    }
}
