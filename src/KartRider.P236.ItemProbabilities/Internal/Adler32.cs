// Adapted from yanygm/Launcher_V2 (AFL-3.0). See ../NOTICE.md.
namespace KartRider.P236.ItemProbabilities.Internal;

internal static class Adler32
{
    private const uint Modulo = 65_521;

    internal static uint Compute(uint initial, ReadOnlySpan<byte> data)
    {
        uint a = initial & 0xffff;
        uint b = initial >> 16;

        // Periodic reduction keeps the additions bounded even for large archives.
        while (!data.IsEmpty)
        {
            int length = Math.Min(data.Length, 5_552);
            ReadOnlySpan<byte> block = data[..length];
            foreach (byte value in block)
            {
                a += value;
                b += a;
            }

            a %= Modulo;
            b %= Modulo;
            data = data[length..];
        }

        return (b << 16) | a;
    }

    internal static uint Continue(uint previous, ReadOnlySpan<byte> data) => Compute(previous, data);
}
