// Adapted from yanygm/Launcher_V2 KRData/BinaryXml handling (AFL-3.0).
// See ../NOTICE.md.
using System.Globalization;

namespace KartRider.P236.ItemProbabilities.Internal;

internal sealed class AaaMetadataDocument
{
    private AaaMetadataDocument(BinaryXmlNode root, KrDataEncoding encoding)
    {
        Root = root;
        Encoding = encoding;
    }

    internal BinaryXmlNode Root { get; }

    internal KrDataEncoding Encoding { get; }

    internal void ValidateSemanticallyEqual(AaaMetadataDocument other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Encoding != other.Encoding)
        {
            throw new InvalidDataException(
                "aaa.pk KRData encoding mismatch: compression mode, encryption mode, or encryption key differs.");
        }

        ValidateNodeSemanticallyEqual(Root, other.Root, "/");
    }

    internal static AaaMetadataDocument Create(BinaryXmlNode root) =>
        new(root, KrDataEncoding.CompressedUnencrypted);

    internal static AaaMetadataDocument Create(
        BinaryXmlNode root,
        KrDataEncoding encoding) => new(root, encoding);

    private static void ValidateNodeSemanticallyEqual(
        BinaryXmlNode expected,
        BinaryXmlNode actual,
        string path)
    {
        if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"aaa.pk BML node name mismatch at '{path}': expected '{expected.Name}', actual '{actual.Name}'.");
        }

        if (!string.Equals(expected.Text, actual.Text, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"aaa.pk BML node text mismatch at '{path}'.");
        }

        if (expected.Attributes.Count != actual.Attributes.Count)
        {
            throw new InvalidDataException(
                $"aaa.pk BML attribute count mismatch at '{path}': expected " +
                $"{expected.Attributes.Count.ToString(CultureInfo.InvariantCulture)}, actual " +
                $"{actual.Attributes.Count.ToString(CultureInfo.InvariantCulture)}.");
        }

        for (int index = 0; index < expected.Attributes.Count; index++)
        {
            BinaryXmlAttribute expectedAttribute = expected.Attributes[index];
            BinaryXmlAttribute actualAttribute = actual.Attributes[index];
            if (!string.Equals(expectedAttribute.Name, actualAttribute.Name, StringComparison.Ordinal) ||
                !string.Equals(expectedAttribute.Value, actualAttribute.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"aaa.pk BML attribute or order mismatch at '{path}' index " +
                    $"{index.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        if (expected.Children.Count != actual.Children.Count)
        {
            throw new InvalidDataException(
                $"aaa.pk BML child count mismatch at '{path}': expected " +
                $"{expected.Children.Count.ToString(CultureInfo.InvariantCulture)}, actual " +
                $"{actual.Children.Count.ToString(CultureInfo.InvariantCulture)}.");
        }

        for (int index = 0; index < expected.Children.Count; index++)
        {
            BinaryXmlNode expectedChild = expected.Children[index];
            BinaryXmlNode actualChild = actual.Children[index];
            string childPath = $"{path.TrimEnd('/')}/{expectedChild.Name}[{index.ToString(CultureInfo.InvariantCulture)}]";
            ValidateNodeSemanticallyEqual(expectedChild, actualChild, childPath);
        }
    }

    internal static AaaMetadataDocument Open(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream);
        if (stream.Length < sizeof(int))
        {
            throw new InvalidDataException("aaa.pk is shorter than its length prefix.");
        }

        int encodedLength = reader.ReadInt32();
        if (encodedLength <= 0 || encodedLength != stream.Length - sizeof(int))
        {
            throw new InvalidDataException("aaa.pk has an invalid encoded length.");
        }

        byte[] bml = KrDataCodec.Read(reader, encodedLength, out KrDataEncoding encoding);
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("aaa.pk contains trailing bytes.");
        }

        return new AaaMetadataDocument(BinaryXmlCodec.Read(bml), encoding);
    }

    internal void UpdateItemArchive(RhoArchiveMetadata metadata)
    {
        UpdateArchive("item.rho", metadata);
    }

    internal void UpdateArchive(string fileName, RhoArchiveMetadata metadata)
    {
        BinaryXmlNode entry = GetArchiveEntry(fileName);
        entry.SetAttribute("key", metadata.Key.ToString(CultureInfo.InvariantCulture));
        entry.SetAttribute("dataHash", metadata.DataHash.ToString(CultureInfo.InvariantCulture));
        entry.SetAttribute("mediaSize", metadata.MediaSize.ToString(CultureInfo.InvariantCulture));
    }

    internal void ValidateItemArchive(RhoArchiveMetadata metadata)
    {
        ValidateArchive("item.rho", metadata);
    }

    internal void ValidateArchive(string fileName, RhoArchiveMetadata metadata)
    {
        BinaryXmlNode entry = GetArchiveEntry(fileName);
        if (entry.GetAttribute("key") != metadata.Key.ToString(CultureInfo.InvariantCulture) ||
            entry.GetAttribute("dataHash") != metadata.DataHash.ToString(CultureInfo.InvariantCulture) ||
            entry.GetAttribute("mediaSize") != metadata.MediaSize.ToString(CultureInfo.InvariantCulture))
        {
            throw new InvalidDataException(
                $"aaa.pk metadata for '{fileName}' does not match the generated archive.");
        }
    }

    internal bool ContainsArchive(string fileName) =>
        FindArchiveEntries(fileName).Count != 0;

    internal void EnsureArchiveEntryFrom(AaaMetadataDocument source, string fileName)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<BinaryXmlNode> existing = FindArchiveEntries(fileName);
        if (existing.Count > 1)
        {
            throw new InvalidDataException(
                $"aaa.pk contains more than one RhoFolder entry for '{fileName}'.");
        }

        if (existing.Count == 1)
        {
            return;
        }

        List<BinaryXmlNode> sourcePath = source.GetArchivePath(fileName);
        if (sourcePath.Count < 2)
        {
            throw new InvalidDataException(
                $"Donor aaa.pk has an invalid hierarchy for '{fileName}'.");
        }

        BinaryXmlNode targetParent = Root;
        for (int index = 1; index < sourcePath.Count - 1; index++)
        {
            BinaryXmlNode sourceFolder = sourcePath[index];
            if (!string.Equals(sourceFolder.Name, "PackFolder", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Donor aaa.pk hierarchy for '{fileName}' contains an unsupported node.");
            }

            string? folderName = sourceFolder.GetAttribute("name");
            BinaryXmlNode? targetFolder = targetParent.Children.FirstOrDefault(child =>
                string.Equals(child.Name, "PackFolder", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(child.GetAttribute("name"), folderName, StringComparison.OrdinalIgnoreCase));
            if (targetFolder is null)
            {
                targetFolder = CloneNode(sourceFolder, includeChildren: false);
                targetParent.Children.Add(targetFolder);
            }

            targetParent = targetFolder;
        }

        targetParent.Children.Add(CloneNode(sourcePath[^1], includeChildren: true));
    }

    internal void SortArchiveEntriesInPackFolder(string folderName)
    {
        List<BinaryXmlNode> matches = BinaryXmlCodec.Enumerate(Root)
            .Where(node =>
                string.Equals(node.Name, "PackFolder", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    node.GetAttribute("name"),
                    folderName,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"aaa.pk must contain exactly one PackFolder named '{folderName}'.");
        }

        BinaryXmlNode folder = matches[0];
        if (folder.Children.Any(child =>
                !string.Equals(child.Name, "RhoFolder", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(child.GetAttribute("fileName"))))
        {
            throw new InvalidDataException(
                $"aaa.pk PackFolder '{folderName}' contains an unsupported child node.");
        }

        List<BinaryXmlNode> ordered = folder.Children
            .OrderBy(
                child => child.GetAttribute("fileName"),
                StringComparer.Ordinal)
            .ToList();
        folder.Children.Clear();
        folder.Children.AddRange(ordered);
    }

    internal void Save(string path)
    {
        byte[] bml = BinaryXmlCodec.Write(Root);
        using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.WriteThrough);
        using BinaryWriter writer = new(stream);
        writer.Write(0);
        int encodedLength = KrDataCodec.Write(writer, bml, Encoding);
        stream.Position = 0;
        writer.Write(encodedLength);
        stream.Flush(flushToDisk: true);
    }

    private BinaryXmlNode GetArchiveEntry(string fileName)
    {
        List<BinaryXmlNode> matches = FindArchiveEntries(fileName);
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"aaa.pk must contain exactly one RhoFolder entry for '{fileName}'.");
        }

        return matches[0];
    }

    private List<BinaryXmlNode> FindArchiveEntries(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("An RHO base file name is required.", nameof(fileName));
        }

        return BinaryXmlCodec.Enumerate(Root)
            .Where(node =>
                string.Equals(node.Name, "RhoFolder", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(node.GetAttribute("fileName"), fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private List<BinaryXmlNode> GetArchivePath(string fileName)
    {
        List<BinaryXmlNode> path = new();
        if (!TryFindArchivePath(Root, fileName, path))
        {
            throw new InvalidDataException(
                $"Donor aaa.pk does not contain a RhoFolder entry for '{fileName}'.");
        }

        return path;
    }

    private static bool TryFindArchivePath(
        BinaryXmlNode node,
        string fileName,
        List<BinaryXmlNode> path)
    {
        path.Add(node);
        if (string.Equals(node.Name, "RhoFolder", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(node.GetAttribute("fileName"), fileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (BinaryXmlNode child in node.Children)
        {
            if (TryFindArchivePath(child, fileName, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static BinaryXmlNode CloneNode(BinaryXmlNode source, bool includeChildren)
    {
        BinaryXmlNode clone = new(source.Name)
        {
            Text = source.Text,
        };
        foreach (BinaryXmlAttribute attribute in source.Attributes)
        {
            clone.Attributes.Add(new BinaryXmlAttribute(attribute.Name, attribute.Value));
        }

        if (includeChildren)
        {
            foreach (BinaryXmlNode child in source.Children)
            {
                clone.Children.Add(CloneNode(child, includeChildren: true));
            }
        }

        return clone;
    }
}
