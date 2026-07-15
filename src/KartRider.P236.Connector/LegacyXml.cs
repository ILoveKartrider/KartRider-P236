using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace KartRider.P236.Connector;

/// <summary>
/// Loads small legacy XML files by inspecting their bytes before honoring the
/// declaration. Some original 2005 files contain ASCII bytes while declaring
/// UTF-16, which a stream-based XmlReader rejects before it can parse the XML.
/// </summary>
internal static class LegacyXml
{
    private const int MaximumFileLength = 4 * 1024 * 1024;

    internal static XDocument Load(
        string path,
        LoadOptions options = LoadOptions.PreserveWhitespace)
    {
        using FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Load(stream, options);
    }

    internal static XDocument Load(
        Stream stream,
        LoadOptions options = LoadOptions.PreserveWhitespace)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        while (true)
        {
            int read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaximumFileLength)
            {
                throw new InvalidDataException("Legacy XML file exceeds the 4 MiB limit.");
            }
            buffer.Write(chunk, 0, read);
        }

        return Load(buffer.ToArray(), options);
    }

    internal static XDocument Load(
        byte[] bytes,
        LoadOptions options = LoadOptions.PreserveWhitespace)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.Length > MaximumFileLength)
        {
            throw new InvalidDataException("Legacy XML file size is outside the allowed range.");
        }

        Encoding encoding;
        int preambleLength;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(false, true);
            preambleLength = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            preambleLength = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            preambleLength = 2;
        }
        else if (LooksLikeUtf16(bytes, littleEndian: true))
        {
            encoding = Encoding.Unicode;
            preambleLength = 0;
        }
        else if (LooksLikeUtf16(bytes, littleEndian: false))
        {
            encoding = Encoding.BigEndianUnicode;
            preambleLength = 0;
        }
        else
        {
            encoding = new UTF8Encoding(false, true);
            preambleLength = 0;
        }

        string xml;
        try
        {
            xml = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (DecoderFallbackException)
        {
            // Legacy account files used the system ANSI code page in a few
            // distributions. Latin-1 preserves the byte values for XML parsing
            // without silently dropping data.
            xml = Encoding.Latin1.GetString(bytes);
        }

        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using StringReader stringReader = new StringReader(xml);
        using XmlReader reader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(reader, options);
    }

    private static bool LooksLikeUtf16(byte[] bytes, bool littleEndian)
    {
        int sampleLength = Math.Min(bytes.Length, 128);
        if (sampleLength < 4)
        {
            return false;
        }

        int expectedNulls = 0;
        int expectedSlots = 0;
        for (int index = littleEndian ? 1 : 0; index < sampleLength; index += 2)
        {
            expectedSlots++;
            if (bytes[index] == 0)
            {
                expectedNulls++;
            }
        }

        return expectedNulls >= Math.Max(2, expectedSlots * 3 / 4);
    }
}
