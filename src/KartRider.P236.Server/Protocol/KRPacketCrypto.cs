using System.Buffers.Binary;

namespace KartRider.Common.Security;

// Clean .NET 8 adaptation of Launcher_V2's AFL-3.0 packet transform.
internal static class KRPacketCrypto
{
    public static uint HashEncrypt(byte[] data, uint length, uint key) => Transform(data, length, key, encrypt: true);
    public static uint HashDecrypt(byte[] data, uint length, uint key) => Transform(data, length, key, encrypt: false);

    private static uint Transform(byte[] data, uint length, uint key, bool encrypt)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
        uint[] words = [key ^ 347277256u, key ^ 2361332396u, key ^ 604215233u, key ^ 4089260480u];
        Span<byte> keyBytes = stackalloc byte[16];
        for (int i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(keyBytes[(i * 4)..], words[i]);

        uint checksum = 0;
        int offset = 0;
        int fullBlocks = checked((int)(length / 16));
        for (int block = 0; block < fullBlocks; block++, offset += 16)
        {
            for (int word = 0; word < 4; word++)
            {
                Span<byte> slice = data.AsSpan(offset + word * 4, 4);
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(slice);
                if (encrypt) checksum ^= value;
                value ^= words[word];
                BinaryPrimitives.WriteUInt32LittleEndian(slice, value);
                if (!encrypt) checksum ^= value;
            }
        }
        for (int i = offset, keyIndex = 0; i < length; i++, keyIndex++)
        {
            if (encrypt) checksum ^= (uint)data[i] << (keyIndex & 31);
            data[i] ^= keyBytes[keyIndex];
            if (!encrypt) checksum ^= (uint)data[i] << (keyIndex & 31);
        }
        return checksum;
    }
}
