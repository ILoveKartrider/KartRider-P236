// Adapted from yanygm/Launcher_V2 DataProcessor (AFL-3.0). See ../NOTICE.md.
using System.IO.Compression;

namespace KartRider.P236.ItemProbabilities.Internal;

internal static class KrDataCodec
{
    private const byte Signature = 0x53;
    private const byte CompressedFlag = 0x01;
    private const byte EncryptedFlag = 0x02;
    private const int MaximumDecodedBytes = 256 * 1024 * 1024;

    internal static byte[] Read(
        BinaryReader reader,
        int encodedLength,
        out KrDataEncoding encoding)
    {
        if (encodedLength <= 0)
        {
            throw new InvalidDataException("KRData encoded length must be positive.");
        }

        long start = reader.BaseStream.Position;
        if (reader.ReadByte() != Signature)
        {
            throw new InvalidDataException("KRData signature is invalid.");
        }

        byte mode = reader.ReadByte();
        if ((mode & ~(CompressedFlag | EncryptedFlag)) != 0)
        {
            throw new InvalidDataException("KRData processing mode is unsupported.");
        }

        uint expectedHash = reader.ReadUInt32();
        bool encrypted = (mode & EncryptedFlag) != 0;
        bool compressed = (mode & CompressedFlag) != 0;
        uint encryptionKey = encrypted ? reader.ReadUInt32() : 0;
        encoding = new KrDataEncoding(compressed, encrypted, encryptionKey);
        int decodedLength = compressed ? reader.ReadInt32() : 0;
        int headerLength = checked((int)(reader.BaseStream.Position - start));
        int payloadLength = encodedLength - headerLength;
        if (payloadLength < 0)
        {
            throw new InvalidDataException("KRData header exceeds its encoded length.");
        }

        byte[] payload = reader.ReadBytes(payloadLength);
        if (payload.Length != payloadLength)
        {
            throw new EndOfStreamException("KRData payload is truncated.");
        }

        if (encrypted)
        {
            RhoCrypto.TransformData(encryptionKey, payload);
        }

        byte[] decoded = compressed ? Decompress(payload, decodedLength) : payload;
        if (Adler32.Compute(0, decoded) != expectedHash)
        {
            throw new InvalidDataException("KRData checksum does not match its payload.");
        }

        return decoded;
    }

    internal static int Write(
        BinaryWriter writer,
        ReadOnlySpan<byte> data,
        KrDataEncoding encoding)
    {
        long start = writer.BaseStream.Position;
        byte[] payload = encoding.Compressed ? Compress(data) : data.ToArray();
        if (encoding.Encrypted)
        {
            RhoCrypto.TransformData(encoding.EncryptionKey, payload);
        }

        writer.Write(Signature);
        byte mode = 0;
        if (encoding.Compressed)
        {
            mode |= CompressedFlag;
        }
        if (encoding.Encrypted)
        {
            mode |= EncryptedFlag;
        }
        writer.Write(mode);
        writer.Write(Adler32.Compute(0, data));
        if (encoding.Encrypted)
        {
            writer.Write(encoding.EncryptionKey);
        }
        if (encoding.Compressed)
        {
            writer.Write(data.Length);
        }

        writer.Write(payload);
        return checked((int)(writer.BaseStream.Position - start));
    }

    internal static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using MemoryStream output = new();
        using (ZLibStream zlib = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    internal static byte[] Decompress(ReadOnlySpan<byte> data, int expectedLength)
    {
        if (expectedLength is < 0 or > MaximumDecodedBytes)
        {
            throw new InvalidDataException("Compressed data declares an invalid decoded size.");
        }

        using MemoryStream input = new(data.ToArray(), writable: false);
        using ZLibStream zlib = new(input, CompressionMode.Decompress);
        byte[] output = new byte[expectedLength];
        zlib.ReadExactly(output);
        if (zlib.ReadByte() != -1)
        {
            throw new InvalidDataException("Compressed data expands beyond its declared size.");
        }

        return output;
    }
}

internal readonly record struct KrDataEncoding(
    bool Compressed,
    bool Encrypted,
    uint EncryptionKey)
{
    internal static KrDataEncoding CompressedUnencrypted => new(true, false, 0);
}
