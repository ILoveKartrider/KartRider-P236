using System.Globalization;
using System.Text;
using KartRider.P236.ItemProbabilities.Internal;

namespace KartRider.P236.ItemProbabilities.Tests;

internal sealed class TestP236Data : IDisposable
{
    internal static readonly KrDataEncoding MetadataEncoding =
        new(Compressed: true, Encrypted: true, EncryptionKey: 0x5A17C3E9u);

    private TestP236Data(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        DataDirectory = Path.Combine(rootDirectory, "Data");
    }

    internal string RootDirectory { get; }

    internal string DataDirectory { get; }

    internal string ItemPath => Path.Combine(DataDirectory, "item.rho");

    internal string MetadataPath => Path.Combine(DataDirectory, "aaa.pk");

    internal static TestP236Data Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "KartRider.P236.ItemProbabilityTests", Guid.NewGuid().ToString("N"));
        TestP236Data fixture = new(root);
        Directory.CreateDirectory(fixture.DataDirectory);

        RhoArchiveDocument archive = RhoArchiveDocument.Create(layerVersion: 1);
        archive.Root.AddFile(new RhoArchiveFile(
            "unrelated.bin",
            Encoding.UTF8.GetBytes("unrelated archive payload\0with binary tail"),
            RhoFileProperty.CompressedEncrypted));
        RhoArchiveFolder slot = archive.Root.AddFolder("slot");
        slot.AddFile(new RhoArchiveFile(
            "itemProb@zz.bml",
            CreateRankBml((101, "바나나", 3, 5, 7), (102, "물폭탄", 11, 13, 17)),
            RhoFileProperty.Encrypted));
        slot.AddFile(new RhoArchiveFile(
            "itemProb_team@zz.bml",
            CreateRankBml((201, "우주선", 0, 4, 6), (202, "자석", 8, 10, 12)),
            RhoFileProperty.Compressed));
        slot.AddFile(new RhoArchiveFile(
            "itemProb_flag@zz.bml",
            CreateRankBml((301, "고스트", 1, 2, 3), (302, "마인", 4, 5, 6), (303, "롤링밤", 7, 8, 9)),
            RhoFileProperty.PartialEncrypted));
        slot.AddFile(new RhoArchiveFile(
            "itemProb_bonus@zz.bml",
            CreateBonusBml((401, "보너스 방패", 23), (402, "보너스 부스터", 29)),
            RhoFileProperty.CompressedEncrypted));
        slot.AddFile(new RhoArchiveFile(
            "itemProb_team_bonus@zz.bml",
            CreateBonusBml((501, "팀 보너스", 31), (502, "팀 천사", 37)),
            RhoFileProperty.None));
        RhoArchiveMetadata metadata = archive.Save(fixture.ItemPath);

        BinaryXmlNode rootNode = new("PackFolder");
        rootNode.SetAttribute("keep", "root-value");
        BinaryXmlNode unrelated = new("RhoFolder");
        unrelated.SetAttribute("fileName", "track.rho");
        unrelated.SetAttribute("key", "123");
        rootNode.Children.Add(unrelated);
        BinaryXmlNode item = new("RhoFolder");
        item.SetAttribute("fileName", "item.rho");
        item.SetAttribute("key", metadata.Key.ToString(CultureInfo.InvariantCulture));
        item.SetAttribute("dataHash", metadata.DataHash.ToString(CultureInfo.InvariantCulture));
        item.SetAttribute("mediaSize", metadata.MediaSize.ToString(CultureInfo.InvariantCulture));
        item.SetAttribute("keep", "item-value");
        rootNode.Children.Add(item);
        AaaMetadataDocument.Create(rootNode, MetadataEncoding).Save(fixture.MetadataPath);
        return fixture;
    }

    internal static ItemProbabilityConfiguration Clone(ItemProbabilityConfiguration source) => new()
    {
        Version = source.Version,
        Individual = source.Individual.Select(Clone).ToList(),
        Team = source.Team.Select(Clone).ToList(),
        Flag = source.Flag.Select(Clone).ToList(),
        IndividualBonus = source.IndividualBonus.Select(Clone).ToList(),
        TeamBonus = source.TeamBonus.Select(Clone).ToList(),
    };

    public void Dispose()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }

    private static ItemProbabilityEntry Clone(ItemProbabilityEntry entry) => new()
    {
        ItemId = entry.ItemId,
        Name = entry.Name,
        HighRank = entry.HighRank,
        MidRank = entry.MidRank,
        LowRank = entry.LowRank,
    };

    private static ItemProbabilityBonusEntry Clone(ItemProbabilityBonusEntry entry) => new()
    {
        ItemId = entry.ItemId,
        Name = entry.Name,
        Weight = entry.Weight,
    };

    private static byte[] CreateRankBml(params (int Id, string Name, int High, int Mid, int Low)[] rows)
    {
        BinaryXmlNode root = new("items");
        foreach ((int id, string name, int high, int mid, int low) in rows)
        {
            BinaryXmlNode item = new("item");
            item.SetAttribute("name", name);
            item.SetAttribute("idx", id.ToString(CultureInfo.InvariantCulture));
            SetWeight(item, "highrank", high);
            SetWeight(item, "midrank", mid);
            SetWeight(item, "lowrank", low);
            item.SetAttribute("unrelated", $"keep-{id.ToString(CultureInfo.InvariantCulture)}");
            root.Children.Add(item);
        }

        return BinaryXmlCodec.Write(root);
    }

    private static void SetWeight(BinaryXmlNode item, string name, int value)
    {
        if (value != 0)
        {
            item.SetAttribute(name, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static byte[] CreateBonusBml(params (int Id, string Name, int Weight)[] rows)
    {
        BinaryXmlNode root = new("items");
        foreach ((int id, string name, int weight) in rows)
        {
            BinaryXmlNode item = new("item");
            item.SetAttribute("name", name);
            item.SetAttribute("idx", id.ToString(CultureInfo.InvariantCulture));
            item.SetAttribute("prob", weight.ToString(CultureInfo.InvariantCulture));
            item.SetAttribute("unrelated", $"keep-{id.ToString(CultureInfo.InvariantCulture)}");
            root.Children.Add(item);
        }

        return BinaryXmlCodec.Write(root);
    }
}
