using System.Text;

namespace KartRider.Common.Utilities;

// Rewritten from the AFL-3.0 Launcher_V2 helper; see ../NOTICE.md.
internal static class Adler32Helper
{
    private const uint Modulus = 65_521;
    public static uint GenerateAdler32(byte[] bytes, uint seed = 0)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        uint a = seed & 0xffff;
        uint b = seed >> 16;
        foreach (byte value in bytes)
        {
            a = (a + value) % Modulus;
            b = (b + a) % Modulus;
        }
        return a | (b << 16);
    }
    public static uint GenerateAdler32_ASCII(string value, uint seed = 0) =>
        GenerateAdler32(Encoding.ASCII.GetBytes(value), seed);
    public static int GenerateSimpleAdler32(byte[] bytes) => unchecked((int)GenerateAdler32(bytes, 1));
}
