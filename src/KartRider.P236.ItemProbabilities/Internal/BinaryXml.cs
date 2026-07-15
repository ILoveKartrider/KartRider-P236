// Adapted from yanygm/Launcher_V2 BinaryXml classes (AFL-3.0). See ../NOTICE.md.
using System.Globalization;
using System.Text;

namespace KartRider.P236.ItemProbabilities.Internal;

internal sealed class BinaryXmlNode
{
    internal BinaryXmlNode(string name)
    {
        Name = name;
    }

    internal string Name { get; set; }

    internal string Text { get; set; } = string.Empty;

    internal List<BinaryXmlAttribute> Attributes { get; } = new();

    internal List<BinaryXmlNode> Children { get; } = new();

    internal string? GetAttribute(string name) =>
        Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    internal void SetAttribute(string name, string value)
    {
        BinaryXmlAttribute? attribute = Attributes.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (attribute is null)
        {
            Attributes.Add(new BinaryXmlAttribute(name, value));
        }
        else
        {
            attribute.Value = value;
        }
    }
}

internal sealed class BinaryXmlAttribute
{
    internal BinaryXmlAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }

    internal string Name { get; }

    internal string Value { get; set; }
}

internal static class BinaryXmlCodec
{
    private const int MaximumDepth = 128;
    private const int MaximumNodes = 1_000_000;
    private const int MaximumAttributesPerNode = 100_000;
    private const int MaximumChildrenPerNode = 1_000_000;
    private const int MaximumStringCharacters = 1_000_000;

    internal static BinaryXmlNode Read(ReadOnlySpan<byte> data)
    {
        using MemoryStream stream = new(data.ToArray(), writable: false);
        using BinaryReader reader = new(stream, Encoding.Unicode, leaveOpen: true);
        int nodeCount = 0;
        BinaryXmlNode root = ReadNode(reader, 0, ref nodeCount);
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("BML contains trailing bytes.");
        }

        return root;
    }

    internal static byte[] Write(BinaryXmlNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.Unicode, leaveOpen: true))
        {
            int nodeCount = 0;
            WriteNode(writer, root, 0, ref nodeCount);
        }

        return stream.ToArray();
    }

    internal static IEnumerable<BinaryXmlNode> Enumerate(BinaryXmlNode root)
    {
        Stack<BinaryXmlNode> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            BinaryXmlNode current = pending.Pop();
            yield return current;
            for (int index = current.Children.Count - 1; index >= 0; index--)
            {
                pending.Push(current.Children[index]);
            }
        }
    }

    private static BinaryXmlNode ReadNode(BinaryReader reader, int depth, ref int nodeCount)
    {
        ValidateNode(depth, checked(++nodeCount));
        BinaryXmlNode node = new(ReadString(reader))
        {
            Text = ReadString(reader),
        };

        int attributeCount = ReadCount(reader, "attribute", MaximumAttributesPerNode);
        for (int index = 0; index < attributeCount; index++)
        {
            node.Attributes.Add(new BinaryXmlAttribute(ReadString(reader), ReadString(reader)));
        }

        int childCount = ReadCount(reader, "child", MaximumChildrenPerNode);
        for (int index = 0; index < childCount; index++)
        {
            node.Children.Add(ReadNode(reader, depth + 1, ref nodeCount));
        }

        return node;
    }

    private static void WriteNode(BinaryWriter writer, BinaryXmlNode node, int depth, ref int nodeCount)
    {
        ValidateNode(depth, checked(++nodeCount));
        ValidateCount(node.Attributes.Count, "attribute", MaximumAttributesPerNode);
        ValidateCount(node.Children.Count, "child", MaximumChildrenPerNode);
        WriteString(writer, node.Name);
        WriteString(writer, node.Text);
        writer.Write(node.Attributes.Count);
        foreach (BinaryXmlAttribute attribute in node.Attributes)
        {
            WriteString(writer, attribute.Name);
            WriteString(writer, attribute.Value);
        }

        writer.Write(node.Children.Count);
        foreach (BinaryXmlNode child in node.Children)
        {
            WriteNode(writer, child, depth + 1, ref nodeCount);
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        int characterCount = reader.ReadInt32();
        if (characterCount is < 0 or > MaximumStringCharacters)
        {
            throw new InvalidDataException(
                $"BML string length {characterCount.ToString(CultureInfo.InvariantCulture)} is invalid.");
        }

        int byteCount = checked(characterCount * sizeof(char));
        byte[] bytes = reader.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
        {
            throw new EndOfStreamException("BML string is truncated.");
        }

        return Encoding.Unicode.GetString(bytes);
    }

    private static void WriteString(BinaryWriter writer, string? value)
    {
        value ??= string.Empty;
        if (value.Length > MaximumStringCharacters)
        {
            throw new InvalidDataException("BML string is too long.");
        }

        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
    }

    private static int ReadCount(BinaryReader reader, string kind, int maximum)
    {
        int count = reader.ReadInt32();
        ValidateCount(count, kind, maximum);
        return count;
    }

    private static void ValidateCount(int count, string kind, int maximum)
    {
        if (count is < 0 || count > maximum)
        {
            throw new InvalidDataException($"BML {kind} count is invalid.");
        }
    }

    private static void ValidateNode(int depth, int count)
    {
        if (depth > MaximumDepth || count > MaximumNodes)
        {
            throw new InvalidDataException("BML nesting or node count exceeds the supported limit.");
        }
    }
}
