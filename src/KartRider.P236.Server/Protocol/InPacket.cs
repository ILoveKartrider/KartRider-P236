using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace KartRider.IO;

internal sealed class InPacket : IDisposable
{
    private byte[]? _buffer;
    private int _position;

    public InPacket(byte[] packet) => _buffer = packet ?? throw new ArgumentNullException(nameof(packet));
    public int Length => Buffer.Length;
    public int Available => Length - _position;
    public int Position
    {
        get => _position;
        set
        {
            if ((uint)value > (uint)Length) throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(InPacket));
    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Available) throw new PacketReadException($"Need {count} bytes; {Available} remain.");
        ReadOnlySpan<byte> value = Buffer.AsSpan(_position, count);
        _position += count;
        return value;
    }

    public byte ReadByte() => Take(1)[0];
    public bool ReadBool() => ReadByte() == 1;
    public byte[] ReadBytes(int count) => Take(count).ToArray();
    public short ReadShort() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort ReadUShort() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int ReadInt() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint ReadUInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());
    public IPEndPoint ReadEndPoint() => new(new IPAddress(ReadBytes(4)), ReadUShort());
    public string ReadString(bool ascii = false)
    {
        int characterCount = ReadInt();
        if (characterCount < 0) throw new PacketReadException("Negative string length.");
        int byteCount = checked(characterCount * (ascii ? 1 : 2));
        return (ascii ? Encoding.ASCII : Encoding.Unicode).GetString(Take(byteCount));
    }
    public byte[] ToArray() => (byte[])Buffer.Clone();
    public void Dispose() => _buffer = null;
}
