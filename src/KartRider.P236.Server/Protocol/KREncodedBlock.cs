using System.IO.Compression;
using System.Net;
using KartRider.Common.Utilities;
using KartRider.IO;

namespace KartRider.Common.Security;

internal static class KREncodedBlock
{
    [Flags]
    internal enum EncodeFlag : byte { ZLib = 1, KartCrypto = 2 }

    public static byte[] Encode(byte[] input, EncodeFlag flags, uint? kartCryptoKey)
    {
        ArgumentNullException.ThrowIfNull(input);
        if ((flags & EncodeFlag.KartCrypto) != 0)
            throw new NotSupportedException("P236 server output does not require KartCrypto blocks.");

        byte[] body = input;
        using OutPacket output = new();
        output.WriteByte((byte)'S');
        output.WriteByte((byte)flags);
        output.WriteUInt(Adler32Helper.GenerateAdler32(input));
        if ((flags & EncodeFlag.ZLib) != 0)
        {
            output.WriteInt(input.Length);
            using MemoryStream compressed = new();
            compressed.WriteByte(0x78);
            compressed.WriteByte(0xDA);
            using (DeflateStream deflate = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                deflate.Write(input);
            Span<byte> checksum = stackalloc byte[4];
            int network = IPAddress.HostToNetworkOrder(Adler32Helper.GenerateSimpleAdler32(input));
            BitConverter.TryWriteBytes(checksum, network);
            compressed.Write(checksum);
            body = compressed.ToArray();
        }
        output.WriteBytes(body);
        return output.ToArray();
    }
}
