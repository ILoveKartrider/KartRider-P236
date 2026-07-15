using System.Text;
using KartRider.P236.ItemProbabilities.Internal;

namespace KartRider.P236.ItemProbabilities.Tests;

public sealed class SemanticValidationTests
{
    [Fact]
    public void RhoValidation_AcceptsExactIndependentCopy()
    {
        RhoArchiveDocument expected = CreateArchive();
        RhoArchiveDocument actual = CreateArchive();

        expected.ValidateSemanticallyEqual(actual);
    }

    [Fact]
    public void RhoValidation_RejectsLayerVersionMismatch()
    {
        RhoArchiveDocument expected = CreateArchive(layerVersion: 1);
        RhoArchiveDocument actual = CreateArchive(layerVersion: 0);

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void RhoValidation_RejectsDecodedDataMismatch()
    {
        RhoArchiveDocument expected = CreateArchive(data: "payload");
        RhoArchiveDocument actual = CreateArchive(data: "different");

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void RhoValidation_RejectsFilePropertyMismatch()
    {
        RhoArchiveDocument expected = CreateArchive(property: RhoFileProperty.Encrypted);
        RhoArchiveDocument actual = CreateArchive(property: RhoFileProperty.CompressedEncrypted);

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void RhoValidation_RejectsFolderNameMismatch()
    {
        RhoArchiveDocument expected = CreateArchive(folderName: "slot");
        RhoArchiveDocument actual = CreateArchive(folderName: "Slot");

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void RhoValidation_RejectsOrderedFolderTreeMismatch()
    {
        RhoArchiveDocument expected = CreateArchive();
        RhoArchiveDocument actual = CreateArchive(reverseFolders: true);

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void AaaValidation_AcceptsExactIndependentCopy()
    {
        AaaMetadataDocument expected = CreateMetadata();
        AaaMetadataDocument actual = CreateMetadata();

        expected.ValidateSemanticallyEqual(actual);
    }

    [Fact]
    public void AaaValidation_RejectsNodeNameMismatch()
    {
        AaaMetadataDocument expected = CreateMetadata(childName: "RhoFolder");
        AaaMetadataDocument actual = CreateMetadata(childName: "rhofolder");

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void AaaValidation_RejectsNodeTextMismatch()
    {
        AaaMetadataDocument expected = CreateMetadata(text: "metadata");
        AaaMetadataDocument actual = CreateMetadata(text: "changed");

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void AaaValidation_RejectsAttributeValueMismatch()
    {
        AaaMetadataDocument expected = CreateMetadata(itemKey: "123");
        AaaMetadataDocument actual = CreateMetadata(itemKey: "124");

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void AaaValidation_RejectsAttributeOrderMismatch()
    {
        AaaMetadataDocument expected = CreateMetadata();
        AaaMetadataDocument actual = CreateMetadata(reverseAttributes: true);

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Fact]
    public void AaaValidation_RejectsChildOrderMismatch()
    {
        AaaMetadataDocument expected = CreateMetadata();
        AaaMetadataDocument actual = CreateMetadata(reverseChildren: true);

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    [Theory]
    [InlineData(false, true, 0x5A17C3E9u)]
    [InlineData(true, false, 0x5A17C3E9u)]
    [InlineData(true, true, 0x5A17C3E8u)]
    public void AaaValidation_RejectsEncodingModeOrKeyMismatch(
        bool compressed,
        bool encrypted,
        uint encryptionKey)
    {
        AaaMetadataDocument expected = CreateMetadata();
        AaaMetadataDocument actual = CreateMetadata(
            encoding: new KrDataEncoding(compressed, encrypted, encryptionKey));

        Assert.Throws<InvalidDataException>(() => expected.ValidateSemanticallyEqual(actual));
    }

    private static RhoArchiveDocument CreateArchive(
        int layerVersion = 1,
        string folderName = "slot",
        string data = "payload",
        RhoFileProperty property = RhoFileProperty.Encrypted,
        bool reverseFolders = false)
    {
        RhoArchiveDocument archive = RhoArchiveDocument.Create(layerVersion);
        RhoArchiveFolder slot = new(folderName, archive.Root);
        slot.AddFile(new RhoArchiveFile("itemProb@zz.bml", Encoding.UTF8.GetBytes(data), property));
        RhoArchiveFolder bonus = new("bonus", archive.Root);
        bonus.AddFile(new RhoArchiveFile("keep.bin", new byte[] { 1, 2, 3 }, RhoFileProperty.None));

        if (reverseFolders)
        {
            archive.Root.Folders.Add(bonus);
            archive.Root.Folders.Add(slot);
        }
        else
        {
            archive.Root.Folders.Add(slot);
            archive.Root.Folders.Add(bonus);
        }

        return archive;
    }

    private static AaaMetadataDocument CreateMetadata(
        string childName = "RhoFolder",
        string text = "metadata",
        string itemKey = "123",
        KrDataEncoding? encoding = null,
        bool reverseAttributes = false,
        bool reverseChildren = false)
    {
        BinaryXmlNode root = new("PackFolder")
        {
            Text = text,
        };
        BinaryXmlNode item = new(childName);
        BinaryXmlAttribute fileName = new("fileName", "item.rho");
        BinaryXmlAttribute key = new("key", itemKey);
        if (reverseAttributes)
        {
            item.Attributes.Add(key);
            item.Attributes.Add(fileName);
        }
        else
        {
            item.Attributes.Add(fileName);
            item.Attributes.Add(key);
        }

        BinaryXmlNode unrelated = new("RhoFolder");
        unrelated.SetAttribute("fileName", "track.rho");
        if (reverseChildren)
        {
            root.Children.Add(unrelated);
            root.Children.Add(item);
        }
        else
        {
            root.Children.Add(item);
            root.Children.Add(unrelated);
        }

        return AaaMetadataDocument.Create(
            root,
            encoding ?? new KrDataEncoding(
                Compressed: true,
                Encrypted: true,
                EncryptionKey: 0x5A17C3E9u));
    }
}
