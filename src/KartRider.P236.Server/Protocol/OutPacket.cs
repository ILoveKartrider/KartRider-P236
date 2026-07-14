using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text;
using KartRider.Common.Utilities;

namespace KartRider.IO;

internal sealed class OutPacket : IDisposable
{
    // Launcher_V2's one-byte wire substitution table. This is the only part of
    // its encoded-number helper needed by P236 room replies.
    private static readonly byte[] EncodedByteTable =
    [
        186,124,112,137,33,22,176,189,181,65,162,87,255,32,245,239,172,127,212,84,41,60,130,170,
        218,78,152,71,203,91,81,52,92,146,240,237,86,63,119,83,106,30,24,5,2,31,8,251,148,193,
        97,113,75,205,196,132,69,55,94,39,234,183,74,241,201,9,179,134,177,77,105,215,206,192,
        126,73,58,164,53,0,89,50,168,109,226,175,111,161,235,158,29,16,188,250,229,173,108,104,
        214,45,72,211,101,171,198,117,224,76,6,223,42,202,102,68,221,200,19,142,118,253,13,57,
        238,80,17,167,14,147,88,98,246,247,232,37,157,210,150,11,67,129,160,48,144,61,163,93,
        213,100,217,178,43,103,66,197,12,231,141,184,243,136,49,156,143,222,131,180,187,25,107,
        155,59,208,116,4,10,233,96,114,40,190,36,56,3,123,138,133,227,166,249,18,110,248,174,
        82,153,51,79,85,220,154,1,122,199,46,242,27,121,15,38,95,120,151,139,149,145,28,252,
        115,244,44,21,216,64,230,185,26,236,159,54,7,90,35,207,204,219,140,195,135,228,23,62,
        169,194,47,99,34,128,165,125,225,254,20,209,182,70,191
    ];

    private MemoryStream? _stream;
    public OutPacket(int capacity = 64) => _stream = new MemoryStream(capacity);
    public OutPacket(string packetName) : this() => WriteUInt(Adler32Helper.GenerateAdler32_ASCII(packetName));
    private MemoryStream Stream => _stream ?? throw new ObjectDisposedException(nameof(OutPacket));
    public int Length => checked((int)Stream.Length);
    public int Position { get => checked((int)Stream.Position); set => Stream.Position = value; }
    public void WriteByte(byte value = 0) => Stream.WriteByte(value);
    public void WriteEncByte(byte value) => WriteByte(EncodedByteTable[value % 255]);
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);
    public void WriteBytes(params byte[] value) => Stream.Write(value);
    public void WriteShort(short value = 0) => Append(value, 2);
    public void WriteUShort(ushort value = 0) => Append(value, 2);
    public void WriteInt(int value = 0) => Append(value, 4);
    public void WriteUInt(uint value = 0) => Append(value, 4);
    public void WriteFloat(float value = 0) => WriteInt(BitConverter.SingleToInt32Bits(value));
    public void WriteString(string value, bool ascii = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteInt(value.Length);
        WriteBytes((ascii ? Encoding.ASCII : Encoding.Unicode).GetBytes(value));
    }
    public void WriteEndPoint(IPEndPoint? endpoint)
    {
        if (endpoint is null) { WriteInt(); WriteUShort(); return; }
        byte[] address = endpoint.Address.MapToIPv4().GetAddressBytes();
        WriteBytes(address);
        WriteUShort(checked((ushort)endpoint.Port));
    }
    public void WriteHexString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string compact = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        if ((compact.Length & 1) != 0) throw new FormatException("Hex strings need an even number of digits.");
        for (int i = 0; i < compact.Length; i += 2)
            WriteByte(byte.Parse(compact.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
    private void Append(long value, int count)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        Stream.Write(bytes[..count]);
    }
    public byte[] ToArray() => Stream.ToArray();
    public void Dispose() { _stream?.Dispose(); _stream = null; }
}
