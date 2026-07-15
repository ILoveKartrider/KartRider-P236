using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using KartRider.P236.Connector;
using Xunit;

namespace KartRider.P236.Connector.Tests;

public sealed class PreparedClientSettingsTests
{
    private const uint PinRtti = 284099454;
    private const uint CryptoKey = 0x1234ABCD;
    private const string OriginalUrl = "http://example.invalid/original-service";
    private const string OriginalEndpoint = "127.0.0.1:39312";
    private const string UpdatedAddress = "192.0.2.10";
    private const ushort UpdatedPort = 40123;

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void Apply_PromotesSyntheticV1AndPreservesEnvelopeAndHeader(byte flags)
    {
        using SyntheticClient client = SyntheticClient.Create(flags);
        string storageRoot = "KartRiderP236Test_" + Guid.NewGuid().ToString("N");
        PreparedPinInfo before = PreparedPinValidator.InspectConfigurable(client.Root);
        Assert.Equal((byte)1, before.Format);
        Assert.Equal(OriginalEndpoint, before.LoginEndpoint);
        Assert.Throws<InvalidDataException>(() => PreparedPinValidator.Validate(client.Root));

        PreparedClientSettingsResult result = PreparedClientSettings.Apply(
            client.Root,
            UpdatedAddress,
            UpdatedPort,
            storageRoot,
            Array.Empty<RunningClientInspection>(),
            client.DocumentsRoot);

        Assert.True(result.Changed);
        Assert.Equal((byte)2, result.PinInfo.Format);
        Assert.Equal($"{UpdatedAddress}:{UpdatedPort}", result.PinInfo.LoginEndpoint);
        Assert.Equal(storageRoot, result.PinInfo.StorageRoot);

        PreparedPinInfo validated = PreparedPinValidator.Validate(client.Root);
        Assert.Equal(result.PinInfo.Format, validated.Format);
        Assert.Equal(result.PinInfo.LocaleId, validated.LocaleId);
        Assert.Equal(result.PinInfo.ClientLocation, validated.ClientLocation);
        Assert.Equal(result.PinInfo.MinorVersion, validated.MinorVersion);
        Assert.Equal(result.PinInfo.LoginEndpoint, validated.LoginEndpoint);
        Assert.Equal(result.PinInfo.LoginEndpoints, validated.LoginEndpoints);
        Assert.Equal(result.PinInfo.StorageRoot, validated.StorageRoot);

        DecodedEnvelope envelope = DecodeEnvelope(File.ReadAllBytes(client.PinPath));
        Assert.Equal(flags, envelope.Flags);
        Assert.Equal((flags & 2) != 0 ? CryptoKey : 0U, envelope.CryptoKey);

        byte[] expectedHeader = client.OriginalHeader.ToArray();
        expectedHeader[4] = 2;
        Assert.Equal(expectedHeader, envelope.Decoded.AsSpan(0, 15).ToArray());
        AssertUpgradedBody(envelope.Decoded, storageRoot);
        Assert.True(Directory.Exists(Path.Combine(client.DocumentsRoot, storageRoot, "스크린샷")));
        Assert.True(Directory.Exists(Path.Combine(client.DocumentsRoot, storageRoot, "라이더데이터")));

        byte[] firstCommittedPin = File.ReadAllBytes(client.PinPath);
        byte[] firstCommittedConfig = File.ReadAllBytes(client.ConfigPath);
        PreparedClientSettingsResult unchanged = PreparedClientSettings.Apply(
            client.Root,
            UpdatedAddress,
            UpdatedPort,
            storageRoot,
            Array.Empty<RunningClientInspection>(),
            client.DocumentsRoot);

        Assert.False(unchanged.Changed);
        Assert.Equal(firstCommittedPin, File.ReadAllBytes(client.PinPath));
        Assert.Equal(firstCommittedConfig, File.ReadAllBytes(client.ConfigPath));
        Assert.False(File.Exists(Path.Combine(client.Root, "KartRider.launcher-settings.transaction")));
        Assert.False(File.Exists(Path.Combine(client.Root, "KartRider.pin.launcher-settings.bak")));
        Assert.False(File.Exists(Path.Combine(client.Root, "KartRider.xml.launcher-settings.bak")));
    }

    [Fact]
    public void Apply_AcceptsBomlessUtf16DeclarationAndMismatchedOriginalEndpoint()
    {
        using SyntheticClient client = SyntheticClient.Create(flags: 0);
        const string legacyXmlEndpoint = "203.0.113.20:39400";
        string legacyXml =
            "<?xml version='1.0' encoding='UTF-16'?>\r\n" +
            "<config>\r\n" +
            $"    <server addr='{legacyXmlEndpoint}'/>\r\n" +
            "    <fullscreenOff/>\r\n" +
            "</config>\r\n";
        File.WriteAllBytes(client.ConfigPath, Encoding.ASCII.GetBytes(legacyXml));

        PreparedPinInfo source = PreparedPinValidator.InspectConfigurable(client.Root);
        Assert.Equal((byte)1, source.Format);
        Assert.Equal(legacyXmlEndpoint, source.LoginEndpoint);
        Assert.Equal(new[] { OriginalEndpoint }, source.LoginEndpoints);
        Assert.Throws<InvalidDataException>(
            () => PreparedPinValidator.InspectConfigurablePair(client.Root));

        string storageRoot = "KartRiderP236LegacyXml_" + Guid.NewGuid().ToString("N");
        PreparedClientSettingsResult result = PreparedClientSettings.Apply(
            client.Root,
            UpdatedAddress,
            UpdatedPort,
            storageRoot,
            Array.Empty<RunningClientInspection>(),
            client.DocumentsRoot);

        Assert.True(result.Changed);
        Assert.Equal($"{UpdatedAddress}:{UpdatedPort}", result.PinInfo.LoginEndpoint);
        Assert.All(
            result.PinInfo.LoginEndpoints,
            endpoint => Assert.Equal($"{UpdatedAddress}:{UpdatedPort}", endpoint));
        Assert.Equal(storageRoot, result.PinInfo.StorageRoot);

        byte[] normalizedXml = File.ReadAllBytes(client.ConfigPath);
        Assert.True(normalizedXml.Length >= 2);
        Assert.Equal(0xFF, normalizedXml[0]);
        Assert.Equal(0xFE, normalizedXml[1]);
        string normalizedText = Encoding.Unicode.GetString(normalizedXml, 2, normalizedXml.Length - 2);
        Assert.Contains("fullscreenOff", normalizedText);
        Assert.Contains($"{UpdatedAddress}:{UpdatedPort}", normalizedText);
        _ = PreparedPinValidator.Validate(client.Root);
        Assert.False(File.Exists(Path.Combine(client.Root, "KartRider.launcher-settings.transaction")));
        Assert.False(File.Exists(Path.Combine(client.Root, "KartRider.pin.launcher-settings.bak")));
        Assert.False(File.Exists(Path.Combine(client.Root, "KartRider.xml.launcher-settings.bak")));
    }

    [Fact]
    public void RecoverIfNeeded_RestoresMismatchedV1BackupAfterPartialCommit()
    {
        using SyntheticClient client = SyntheticClient.Create(flags: 0);
        const string legacyXmlEndpoint = "203.0.113.20:39400";
        byte[] originalXml = Encoding.ASCII.GetBytes(
            "<?xml version='1.0' encoding='UTF-16'?>\r\n" +
            $"<config><server addr='{legacyXmlEndpoint}'/><fullscreenOff/></config>\r\n");
        File.WriteAllBytes(client.ConfigPath, originalXml);
        byte[] originalPin = File.ReadAllBytes(client.PinPath);

        string marker = Path.Combine(client.Root, "KartRider.launcher-settings.transaction");
        string pinBackup = Path.Combine(client.Root, "KartRider.pin.launcher-settings.bak");
        string configBackup = Path.Combine(client.Root, "KartRider.xml.launcher-settings.bak");
        File.WriteAllBytes(pinBackup, originalPin);
        File.WriteAllBytes(configBackup, originalXml);
        File.WriteAllText(marker, "version=1", Encoding.UTF8);

        File.WriteAllText(
            client.ConfigPath,
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
            "<config><server addr=\"192.0.2.99:49999\"/></config>",
            Encoding.Unicode);

        PreparedClientSettings.RecoverIfNeeded(client.Root);

        Assert.Equal(originalPin, File.ReadAllBytes(client.PinPath));
        Assert.Equal(originalXml, File.ReadAllBytes(client.ConfigPath));
        Assert.Equal(
            legacyXmlEndpoint,
            PreparedPinValidator.InspectConfigurable(client.Root).LoginEndpoint);
        Assert.False(File.Exists(marker));
        Assert.False(File.Exists(pinBackup));
        Assert.False(File.Exists(configBackup));
    }

    [Fact]
    public void Apply_FailsClosedBeforeMutationWhenRunningProcessCannotBeInspected()
    {
        using SyntheticClient client = SyntheticClient.Create(flags: 0);
        byte[] originalPin = File.ReadAllBytes(client.PinPath);
        byte[] originalConfig = File.ReadAllBytes(client.ConfigPath);
        string storageRoot = "KartRiderP236Blocked_" + Guid.NewGuid().ToString("N");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            PreparedClientSettings.Apply(
                client.Root,
                UpdatedAddress,
                UpdatedPort,
                storageRoot,
                new[]
                {
                    new RunningClientInspection(
                        ProcessId: 4242,
                        ExecutablePath: null,
                        HasExited: false,
                        InspectionFailed: true)
                },
                client.DocumentsRoot));

        Assert.Contains("동일한 권한", exception.Message);
        Assert.Equal(originalPin, File.ReadAllBytes(client.PinPath));
        Assert.Equal(originalConfig, File.ReadAllBytes(client.ConfigPath));
        Assert.False(File.Exists(Path.Combine(client.Root, "KartRider.launcher-settings.transaction")));
    }

    [Fact]
    public void Apply_DoesNotRecoverInterruptedFilesWhileProcessInspectionIsBlocked()
    {
        using SyntheticClient client = SyntheticClient.Create(flags: 0);
        string marker = Path.Combine(client.Root, "KartRider.launcher-settings.transaction");
        string pinBackup = Path.Combine(client.Root, "KartRider.pin.launcher-settings.bak");
        string configBackup = Path.Combine(client.Root, "KartRider.xml.launcher-settings.bak");
        File.Copy(client.PinPath, pinBackup);
        File.Copy(client.ConfigPath, configBackup);
        File.WriteAllText(marker, "version=1", Encoding.UTF8);
        File.WriteAllText(
            client.ConfigPath,
            "<?xml version=\"1.0\" encoding=\"utf-16\"?><config><server addr=\"203.0.113.1:1\" /></config>",
            Encoding.Unicode);
        byte[] interruptedPin = File.ReadAllBytes(client.PinPath);
        byte[] interruptedConfig = File.ReadAllBytes(client.ConfigPath);

        Assert.Throws<InvalidOperationException>(() =>
            PreparedClientSettings.Apply(
                client.Root,
                UpdatedAddress,
                UpdatedPort,
                "KartRiderP236RecoveryBlocked",
                new[]
                {
                    new RunningClientInspection(4243, null, HasExited: false, InspectionFailed: true)
                },
                client.DocumentsRoot));

        Assert.Equal(interruptedPin, File.ReadAllBytes(client.PinPath));
        Assert.Equal(interruptedConfig, File.ReadAllBytes(client.ConfigPath));
        Assert.True(File.Exists(marker));
        Assert.True(File.Exists(pinBackup));
        Assert.True(File.Exists(configBackup));
    }

    [Fact]
    public void RunningClientGuard_AllowsExitedOrOtherReadableProcessOnly()
    {
        string selected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "selected", "KartRider.exe"));
        string other = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "other", "KartRider.exe"));

        PreparedClientSettings.EnsureNoRunningClientMutation(
            selected,
            new[]
            {
                new RunningClientInspection(1, null, HasExited: true, InspectionFailed: true),
                new RunningClientInspection(2, other, HasExited: false, InspectionFailed: false)
            });

        Assert.Throws<InvalidOperationException>(() =>
            PreparedClientSettings.EnsureNoRunningClientMutation(
                selected,
                new[]
                {
                    new RunningClientInspection(3, selected, HasExited: false, InspectionFailed: false)
                }));
    }

    private static void AssertUpgradedBody(byte[] decoded, string storageRoot)
    {
        TestCursor cursor = new TestCursor(decoded, position: 15);
        Assert.Equal((byte)2, cursor.ReadByte());
        Assert.Equal(string.Empty, cursor.ReadString());
        Assert.Equal(OriginalUrl, cursor.ReadString());
        Assert.Equal(string.Empty, cursor.ReadString());

        Assert.Equal(1, cursor.ReadInt32());
        Assert.Equal((byte)1, cursor.ReadByte());
        Assert.Equal("Default", cursor.ReadString());
        Assert.Equal((byte)0, cursor.ReadByte());
        Assert.Equal(1, cursor.ReadInt32());
        Assert.Equal(new byte[] { 192, 0, 2, 10 }, cursor.ReadBytes(4));
        Assert.Equal(UpdatedPort, cursor.ReadUInt16());
        Assert.Equal((byte)0, cursor.ReadByte());

        Assert.Equal((byte)1, cursor.ReadByte());
        BmlNode storage = ReadBml(cursor);
        Assert.Equal("storage", storage.Name);
        Assert.Equal(string.Empty, storage.Value);
        Assert.Empty(storage.Values);
        BmlNode document = Assert.Single(storage.Children);
        Assert.Equal("document", document.Name);
        Assert.Equal(storageRoot, document.Values["root"]);
        Assert.Equal("스크린샷", document.Values["screenShot"]);
        Assert.Equal("라이더데이터", document.Values["riderData"]);
        Assert.Empty(document.Children);
        Assert.Equal((byte)0, cursor.ReadByte());
        Assert.Equal(0, cursor.Remaining);
    }

    private static BmlNode ReadBml(TestCursor cursor)
    {
        BmlNode node = new BmlNode(cursor.ReadString(), cursor.ReadString());
        int valueCount = cursor.ReadInt32();
        for (int index = 0; index < valueCount; index++)
        {
            node.Values.Add(cursor.ReadString(), cursor.ReadString());
        }

        int childCount = cursor.ReadInt32();
        for (int index = 0; index < childCount; index++)
        {
            node.Children.Add(ReadBml(cursor));
        }
        return node;
    }

    private static byte[] CreateV1Decoded(out byte[] header)
    {
        header = new byte[15];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), PinRtti);
        header[4] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5, 2), 1002);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(7, 2), 118);
        header[9] = 0x7A;
        header[10] = 0xA5;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(11, 2), 236);
        header[13] = 0xBC;
        header[14] = 0xDE;

        using MemoryStream stream = new MemoryStream();
        stream.Write(header);
        WriteString(stream, OriginalUrl);
        WriteInt32(stream, 1);
        stream.Write(new byte[] { 127, 0, 0, 1 });
        WriteUInt16(stream, 39312);
        return stream.ToArray();
    }

    private static byte[] EncodeEnvelope(byte[] decoded, byte flags)
    {
        byte[] payload = decoded.ToArray();
        if ((flags & 1) != 0)
        {
            using MemoryStream compressed = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(payload);
            }
            payload = compressed.ToArray();
        }

        if ((flags & 2) != 0)
        {
            ApplyKartCrypto(payload, CryptoKey);
        }

        using MemoryStream encoded = new MemoryStream();
        encoded.WriteByte(0x53);
        encoded.WriteByte(flags);
        WriteUInt32(encoded, GenerateKartAdler32(decoded));
        if ((flags & 2) != 0)
        {
            WriteUInt32(encoded, CryptoKey);
        }
        if ((flags & 1) != 0)
        {
            WriteInt32(encoded, decoded.Length);
        }
        encoded.Write(payload);

        byte[] block = encoded.ToArray();
        using MemoryStream file = new MemoryStream();
        WriteInt32(file, block.Length);
        file.Write(block);
        return file.ToArray();
    }

    private static DecodedEnvelope DecodeEnvelope(byte[] file)
    {
        TestCursor outer = new TestCursor(file);
        int encodedLength = outer.ReadInt32();
        Assert.Equal(encodedLength, outer.Remaining);
        TestCursor encoded = new TestCursor(outer.ReadBytes(encodedLength));
        Assert.Equal((byte)0x53, encoded.ReadByte());
        byte flags = encoded.ReadByte();
        uint checksum = encoded.ReadUInt32();
        uint key = (flags & 2) != 0 ? encoded.ReadUInt32() : 0;
        int expectedLength = (flags & 1) != 0 ? encoded.ReadInt32() : encoded.Remaining;
        byte[] payload = encoded.ReadBytes(encoded.Remaining);

        if ((flags & 2) != 0)
        {
            ApplyKartCrypto(payload, key);
        }

        byte[] decoded;
        if ((flags & 1) != 0)
        {
            using MemoryStream input = new MemoryStream(payload, writable: false);
            using ZLibStream zlib = new ZLibStream(input, CompressionMode.Decompress);
            using MemoryStream output = new MemoryStream();
            zlib.CopyTo(output);
            decoded = output.ToArray();
        }
        else
        {
            decoded = payload;
        }

        Assert.Equal(expectedLength, decoded.Length);
        Assert.Equal(checksum, GenerateKartAdler32(decoded));
        return new DecodedEnvelope(flags, key, decoded);
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
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(wordOffset, 4), value ^ state[word]);
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

    private static void WriteString(Stream stream, string value)
    {
        WriteInt32(stream, value.Length);
        stream.Write(Encoding.Unicode.GetBytes(value));
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private sealed class SyntheticClient : IDisposable
    {
        private SyntheticClient(string root, byte[] originalHeader)
        {
            Root = root;
            OriginalHeader = originalHeader;
        }

        internal string Root { get; }
        internal byte[] OriginalHeader { get; }
        internal string PinPath => Path.Combine(Root, "KartRider.pin");
        internal string ConfigPath => Path.Combine(Root, "KartRider.xml");
        internal string DocumentsRoot => Path.Combine(Root, "Documents");

        internal static SyntheticClient Create(byte flags)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "KartRider.P236.Connector.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            byte[] decoded = CreateV1Decoded(out byte[] header);
            File.WriteAllBytes(Path.Combine(root, "KartRider.pin"), EncodeEnvelope(decoded, flags));
            File.WriteAllText(
                Path.Combine(root, "KartRider.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-16\"?><config><server addr=\"" +
                OriginalEndpoint +
                "\" /></config>",
                Encoding.Unicode);
            return new SyntheticClient(root, header);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed record DecodedEnvelope(byte Flags, uint CryptoKey, byte[] Decoded);

    private sealed class BmlNode
    {
        internal BmlNode(string name, string value)
        {
            Name = name;
            Value = value;
        }

        internal string Name { get; }
        internal string Value { get; }
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        internal List<BmlNode> Children { get; } = new();
    }

    private sealed class TestCursor
    {
        private readonly byte[] _data;
        private int _position;

        internal TestCursor(byte[] data, int position = 0)
        {
            _data = data;
            _position = position;
        }

        internal int Remaining => _data.Length - _position;

        internal byte ReadByte()
        {
            Ensure(1);
            return _data[_position++];
        }

        internal ushort ReadUInt16()
        {
            Ensure(2);
            ushort result = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position, 2));
            _position += 2;
            return result;
        }

        internal uint ReadUInt32()
        {
            Ensure(4);
            uint result = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_position, 4));
            _position += 4;
            return result;
        }

        internal int ReadInt32()
        {
            Ensure(4);
            int result = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_position, 4));
            _position += 4;
            return result;
        }

        internal string ReadString()
        {
            int characters = ReadInt32();
            int byteCount = checked(characters * 2);
            Ensure(byteCount);
            string result = Encoding.Unicode.GetString(_data, _position, byteCount);
            _position += byteCount;
            return result;
        }

        internal byte[] ReadBytes(int count)
        {
            Ensure(count);
            byte[] result = _data.AsSpan(_position, count).ToArray();
            _position += count;
            return result;
        }

        private void Ensure(int count)
        {
            if (count < 0 || count > Remaining)
            {
                throw new InvalidDataException("Synthetic PIN cursor ran past the end of the buffer.");
            }
        }
    }
}
