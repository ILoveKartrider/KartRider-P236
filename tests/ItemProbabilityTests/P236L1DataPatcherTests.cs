using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KartRider.P236.ItemProbabilities.Internal;

namespace KartRider.P236.ItemProbabilities.Tests;

public sealed class P236L1DataPatcherTests
{
    private static readonly string[] ExistingArchives =
    [
        "sound_bgm_factory.rho", "sound_fx_surround.rho",
        "stage_challengeReady.rho", "stage_common.rho",
        "theme_common.rho", "theme_factory.rho", "theme_forest.rho",
        "theme_ice.rho", "theme_northeu.rho", "theme_village.rho",
        "track_common.rho", "zeta_kr_content.rho",
    ];

    private static readonly string[] AddedTracks =
    [
        "track_factory_I05.rho", "track_factory_I06.rho", "track_factory_I07.rho",
        "track_factory_R01.rho", "track_factory_R02.rho", "track_forest_F01.rho",
        "track_forest_I05.rho", "track_forest_I06.rho", "track_forest_I07.rho",
        "track_forest_R04.rho", "track_forest_R05.rho", "track_ice_C001.rho",
        "track_northeu_I05.rho", "track_northeu_I06.rho", "track_northeu_I07.rho",
        "track_northeu_I08.rho", "track_northeu_I09.rho", "track_northeu_R01.rho",
        "track_northeu_R02.rho", "track_village_C114.rho",
        "track_village_C115.rho", "track_village_C116.rho",
    ];

    private static readonly string[] CatalogTracks =
    [
        "northeu_I05", "northeu_I06", "northeu_I07", "northeu_I08", "northeu_I09",
        "northeu_R01", "northeu_R02",
        "factory_I05", "factory_I06", "factory_I07", "factory_R01", "factory_R02",
        "forest_F01", "forest_I05", "forest_I06", "forest_I07", "forest_R04", "forest_R05",
    ];

    private static readonly string[] TrackReverse =
    ["northeu_I05", "northeu_I06", "northeu_I07"];

    private static readonly string[] TrackLocaleReverse =
    [
        "northeu_I05", "northeu_I06", "northeu_I07", "northeu_R01",
        "factory_R01", "forest_I05", "forest_I06", "forest_R04",
    ];

    private static readonly IReadOnlyDictionary<string, (int Missing, string[] Conflicts)>
        ThemeFingerprints = new Dictionary<string, (int, string[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["theme_common.rho"] = (209,
            [
                "texture/G_Sign.dds", "texture/sign-danger-1.png", "texture/t_sign_ani_g.dds",
            ]),
            ["theme_factory.rho"] = (49, []),
            ["theme_forest.rho"] = (43, []),
            ["theme_ice.rho"] = (80,
            [
                "texture/fence_xmasA.dds", "texture/fence_xmasB.dds",
                "texture/sing_ice_CA.png", "texture/sing_ice_CA_1.png",
            ]),
            ["theme_northeu.rho"] = (117, []),
            ["theme_village.rho"] = (73, []),
        };

    private static readonly string[] SoundFx =
    [
        "FM컴퓨터2.ogg", "FM컴퓨터3.ogg", "mo_금속삐그덕.ogg", "mo_밧줄.ogg", "mo_트램.ogg",
        "강물2.ogg", "경기장.ogg", "경기장2.ogg", "공장컨베어04.ogg", "나뭇잎바람.ogg",
        "노르테유_기차.ogg", "새소리바람.ogg", "숲속새.ogg", "우주선점프대.ogg", "터널02.ogg", "팬더.ogg",
    ];

    [Fact]
    public void ApplyBuildsFromSyntheticDonorIsIdempotentAndRestores()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "KartRider.P236.L1DataPatcherTests",
            Guid.NewGuid().ToString("N"));
        string donorRoot = Path.Combine(root, "donor");
        string targetRoot = Path.Combine(root, "target");
        string donorData = Path.Combine(donorRoot, "Data");
        string targetData = Path.Combine(targetRoot, "Data");
        Directory.CreateDirectory(donorData);
        Directory.CreateDirectory(targetData);
        try
        {
            CreateSyntheticTarget(targetData);
            CreateSyntheticDonor(donorData);
            Dictionary<string, string> originalHashes = Directory.GetFiles(targetData)
                .ToDictionary(
                    path => Path.GetFileName(path)!,
                    Hash,
                    StringComparer.OrdinalIgnoreCase);
            SortedDictionary<string, string> originalTree = SnapshotTree(targetData);

            InvalidDataException unsupportedDonor = Assert.Throws<InvalidDataException>(() =>
                P236L1DataPatcher.Apply(donorRoot, targetRoot));
            Assert.Contains("donor 원본과 일치하지 않는 파일", unsupportedDonor.Message);
            SortedDictionary<string, string> rejectedTree = SnapshotTree(targetData);
            Assert.Equal(originalTree.Count, rejectedTree.Count);
            foreach ((string path, string fingerprint) in originalTree)
            {
                Assert.True(rejectedTree.TryGetValue(path, out string? actual));
                Assert.Equal(fingerprint, actual);
            }

            P236L1DataPatchResult first = P236L1DataPatcher.ApplyForTesting(
                donorRoot,
                targetRoot);
            Assert.True(first.Changed);
            Assert.Equal(36, first.ChangedFileCount);
            Assert.All(AddedTracks, name => Assert.True(File.Exists(Path.Combine(targetData, name))));

            RhoArchiveDocument trackCommon = RhoArchiveDocument.Open(
                Path.Combine(targetData, "track_common.rho"));
            BinaryXmlNode challenges = ReadBml(trackCommon, "challenge.bml");
            for (int mission = 0x41; mission <= 0x46; mission++)
            {
                BinaryXmlNode row = Assert.Single(challenges.Children.Where(node =>
                    node.GetAttribute("id") == $"0x{mission:X2}"));
                Assert.Equal("3", row.GetAttribute("type"));
                Assert.Equal("1", row.GetAttribute("lap"));
                Assert.Null(row.GetAttribute("oneTime"));
                Assert.Null(row.GetAttribute("isGetLicense"));
            }

            BinaryXmlNode mission41 = Assert.Single(challenges.Children.Where(node =>
                node.GetAttribute("id") == "0x41"));
            Assert.Equal("0x34", mission41.GetAttribute("require"));
            BinaryXmlNode mission42 = Assert.Single(challenges.Children.Where(node =>
                node.GetAttribute("id") == "0x42"));
            Assert.Equal("rocket", mission42.GetAttribute("loadItem"));
            Assert.Equal("rocket", mission42.GetAttribute("cubeItem"));
            BinaryXmlNode mission44 = Assert.Single(challenges.Children.Where(node =>
                node.GetAttribute("id") == "0x44"));
            Assert.Equal("true", mission44.GetAttribute("showArrow"));
            Assert.Null(mission44.GetAttribute("arrow"));

            BinaryXmlNode challengeLocale = ReadBml(trackCommon, "challengeLocale@kr.bml");
            BinaryXmlNode passYet = Assert.Single(challengeLocale.Children.Where(node =>
                node.Name == "passMsg" && node.GetAttribute("k") == "4_yet"));
            Assert.Contains("L1 라이센스 코스", passYet.GetAttribute("v"));
            BinaryXmlNode passGet = Assert.Single(challengeLocale.Children.Where(node =>
                node.Name == "passMsg" && node.GetAttribute("k") == "4_get"));
            Assert.Contains("모두 완료", passGet.GetAttribute("v"));

            BinaryXmlNode trackCatalog = ReadBml(trackCommon, "track.bml");
            Assert.Equal(
                CatalogTracks,
                trackCatalog.Children
                    .Where(node => node.Name == "track" && CatalogTracks.Contains(node.GetAttribute("id")))
                    .Select(node => node.GetAttribute("id")));
            int firstReverse = trackCatalog.Children.FindIndex(node => node.Name == "track_rvs");
            int firstAddedTrack = trackCatalog.Children.FindIndex(node =>
                node.Name == "track" && node.GetAttribute("id") == CatalogTracks[0]);
            Assert.True(firstAddedTrack < firstReverse);
            Assert.Equal(
                TrackReverse,
                trackCatalog.Children.TakeLast(TrackReverse.Length)
                    .Select(node => node.GetAttribute("refId")));

            RhoArchiveDocument stageCommon = RhoArchiveDocument.Open(
                Path.Combine(targetData, "stage_common.rho"));
            BinaryXmlNode iconCatalog = ReadBml(stageCommon, "icon/icon.bml");
            string[] expectedIcons = Enumerable.Range(1, 4)
                .SelectMany(suffix => new[] { "고가", "다운힐", "대마왕", "로두마니", "타임어택" }
                    .Select(category => $"라이센스1{category}_{suffix}"))
                .ToArray();
            Assert.Equal(expectedIcons, iconCatalog.Children.TakeLast(20).Select(node => node.Name));
            RhoArchiveFolder generatedIconFolder = Assert.Single(stageCommon.Root.Folders.Where(folder =>
                folder.Name == "icon"));
            Assert.Equal(
                generatedIconFolder.Files
                    .OrderBy(file => file.NameWithoutExtension, StringComparer.Ordinal)
                    .Select(file => file.Name),
                generatedIconFolder.Files.Select(file => file.Name));

            foreach ((string themeName, (int missingCount, string[] conflicts)) in ThemeFingerprints)
            {
                RhoArchiveDocument theme = RhoArchiveDocument.Open(Path.Combine(targetData, themeName));
                RhoArchiveFolder texture = Assert.Single(theme.Root.Folders.Where(folder =>
                    folder.Name == "texture"));
                Assert.Equal(
                    missingCount + conflicts.Length + 1 +
                    (themeName == "theme_forest.rho" ? 1 : 0),
                    texture.Files.Count);
                Assert.Equal(
                    texture.Files
                        .OrderBy(file => file.NameWithoutExtension, StringComparer.Ordinal)
                        .Select(file => file.Name),
                    texture.Files.Select(file => file.Name));
            }

            RhoArchiveDocument soundFx = RhoArchiveDocument.Open(
                Path.Combine(targetData, "sound_fx_surround.rho"));
            Assert.Equal(
                soundFx.Root.Files
                    .OrderBy(file => file.NameWithoutExtension, StringComparer.Ordinal)
                    .Select(file => file.Name),
                soundFx.Root.Files.Select(file => file.Name));

            RhoArchiveDocument challengeReady = RhoArchiveDocument.Open(
                Path.Combine(targetData, "stage_challengeReady.rho"));
            for (int mission = 0x41; mission <= 0x46; mission++)
            {
                RhoArchiveFile? description =
                    challengeReady.Root.GetFile($"{mission:X2}/descript.bml");
                Assert.NotNull(description);
                BinaryXmlNode descriptionRoot = BinaryXmlCodec.Read(description.Data);
                Assert.Equal("Container", descriptionRoot.Name);
                Assert.Contains(descriptionRoot.Children, child =>
                    child.Name == "Panel" && child.GetAttribute("texture") == "타이틀");
            }

            BinaryXmlNode mission42Description = BinaryXmlCodec.Read(
                challengeReady.Root.GetFile("42/descript.bml")!.Data);
            Assert.Contains(
                BinaryXmlCodec.Enumerate(mission42Description),
                node => node.GetAttribute("v")?.Contains("디지니와 아이템 대결") == true);

            RhoArchiveDocument zeta = RhoArchiveDocument.Open(
                Path.Combine(targetData, "zeta_kr_content.rho"));
            BinaryXmlNode config = ReadBml(zeta, "config.bml");
            Assert.Equal(
                "4",
                Assert.Single(config.Children.Where(node =>
                    node.GetAttribute("name") == "maxLicenseLv")).GetAttribute("value"));

            AaaMetadataDocument metadata = AaaMetadataDocument.Open(
                Path.Combine(targetData, "aaa.pk"));
            foreach (string archive in ExistingArchives.Concat(AddedTracks))
            {
                metadata.ValidateArchive(
                    archive,
                    RhoArchiveDocument.ReadMetadata(Path.Combine(targetData, archive)));
            }

            BinaryXmlNode trackPackFolder = Assert.Single(BinaryXmlCodec.Enumerate(metadata.Root).Where(node =>
                node.Name == "PackFolder" && node.GetAttribute("name") == "track"));
            Assert.Equal(
                trackPackFolder.Children
                    .OrderBy(node => node.GetAttribute("fileName"), StringComparer.Ordinal)
                    .Select(node => node.GetAttribute("fileName")),
                trackPackFolder.Children.Select(node => node.GetAttribute("fileName")));

            P236L1DataPatchResult second = P236L1DataPatcher.ApplyForTesting(
                donorData,
                targetData);
            Assert.False(second.Changed);
            Assert.Equal(0, second.ChangedFileCount);

            P236L1DataPatchResult restored = P236L1DataPatcher.Restore(targetRoot);
            Assert.True(restored.Changed);
            foreach ((string name, string hash) in originalHashes)
            {
                Assert.Equal(hash, Hash(Path.Combine(targetData, name)));
            }

            Assert.All(AddedTracks, name => Assert.False(File.Exists(Path.Combine(targetData, name))));
            Assert.False(File.Exists(
                Path.Combine(targetData, P236L1DataTransaction.InstallFileName)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CreateSyntheticTarget(string data)
    {
        Dictionary<string, RhoArchiveMetadata> metadata = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in ExistingArchives)
        {
            metadata[name] = SaveArchive(data, name, archive =>
            {
                switch (name)
                {
                    case "stage_common.rho":
                    {
                        RhoArchiveFolder icon = archive.Root.AddFolder("icon");
                        BinaryXmlNode icons = new("icons");
                        icons.Children.Add(new BinaryXmlNode("메뉴라이센스1"));
                        icon.AddFile(Bml("icon.bml", icons));
                        break;
                    }
                    case "track_common.rho":
                        AddTargetTrackCommon(archive);
                        break;
                    case "zeta_kr_content.rho":
                    {
                        BinaryXmlNode root = new("contentList");
                        root.Children.Add(Node(
                            "content",
                            ("name", "maxLicenseLv"),
                            ("value", "3")));
                        archive.Root.AddFile(Bml("config.bml", root));
                        break;
                    }
                    case string theme when ThemeFingerprints.ContainsKey(theme):
                        AddSyntheticThemeTarget(archive, theme);
                        break;
                    default:
                        archive.Root.AddFile(new RhoArchiveFile(
                            "target.keep",
                            Encoding.UTF8.GetBytes(name),
                            RhoFileProperty.None));
                        break;
                }
            });
        }

        SaveMetadata(Path.Combine(data, "aaa.pk"), metadata);
    }

    private static void CreateSyntheticDonor(string data)
    {
        Dictionary<string, RhoArchiveMetadata> metadata = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in ExistingArchives)
        {
            metadata[name] = SaveArchive(data, name, archive =>
            {
                switch (name)
                {
                    case "sound_bgm_factory.rho":
                        archive.Root.AddFile(ArchiveFile("BGM-공장03.ogg", "bgm"));
                        break;
                    case "sound_fx_surround.rho":
                        foreach (string sound in SoundFx)
                        {
                            archive.Root.AddFile(ArchiveFile(sound, sound));
                        }
                        break;
                    case "stage_challengeReady.rho":
                        AddDonorChallengeReady(archive);
                        break;
                    case "stage_common.rho":
                        AddDonorStageCommon(archive);
                        break;
                    case "track_common.rho":
                        AddDonorTrackCommon(archive);
                        break;
                    case string theme when ThemeFingerprints.ContainsKey(theme):
                        AddSyntheticThemeDonor(archive, theme);
                        break;
                    default:
                        archive.Root.AddFile(ArchiveFile("donor.add", name));
                        break;
                }
            });
        }

        foreach (string name in AddedTracks)
        {
            metadata[name] = SaveArchive(data, name, archive =>
                archive.Root.AddFile(ArchiveFile("track.1s", name)));
        }

        _ = SaveArchive(data, "track_forest_B01.rho", archive =>
            archive.Root.AddFile(ArchiveFile("F_wood_d02.png", "floor")));
        SaveMetadata(Path.Combine(data, "aaa.pk"), metadata);
    }

    private static void AddSyntheticThemeTarget(
        RhoArchiveDocument archive,
        string archiveName)
    {
        AddArchivePath(archive.Root, "texture/shared.bin", "shared");
        foreach (string path in ThemeFingerprints[archiveName].Conflicts)
        {
            AddArchivePath(archive.Root, path, "target-" + path);
        }
    }

    private static void AddSyntheticThemeDonor(
        RhoArchiveDocument archive,
        string archiveName)
    {
        (int missingCount, string[] conflicts) = ThemeFingerprints[archiveName];
        AddArchivePath(archive.Root, "texture/shared.bin", "shared");
        foreach (string path in conflicts)
        {
            AddArchivePath(archive.Root, path, "donor-" + path);
        }

        for (int index = 0; index < missingCount; index++)
        {
            AddArchivePath(
                archive.Root,
                $"texture/add-{index:D3}.bin",
                $"{archiveName}-{index}");
        }
    }

    private static void AddArchivePath(
        RhoArchiveFolder root,
        string path,
        string value)
    {
        string[] parts = path.Split('/');
        RhoArchiveFolder folder = root;
        foreach (string part in parts[..^1])
        {
            RhoArchiveFolder? child = folder.Folders.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, part, StringComparison.OrdinalIgnoreCase));
            folder = child ?? folder.AddFolder(part);
        }

        folder.AddFile(ArchiveFile(parts[^1], value));
    }

    private static void AddTargetTrackCommon(RhoArchiveDocument archive)
    {
        BinaryXmlNode challenge = new("challengeList");
        challenge.Children.Add(Node("challenge", ("id", "0x34"), ("type", "3")));
        archive.Root.AddFile(Bml("challenge.bml", challenge));

        BinaryXmlNode challengeLocale = new("challengeList");
        challengeLocale.Children.Add(Node("passMsg", ("k", "3_get"), ("v", "L2")));
        challengeLocale.Children.Add(Node("challenge", ("id", "0x34"), ("name", "L2")));
        archive.Root.AddFile(Bml("challengeLocale@kr.bml", challengeLocale));
        BinaryXmlNode tracks = new("trackList");
        tracks.Children.Add(Node("track", ("id", "baseline")));
        tracks.Children.Add(Node("track_rvs", ("refId", "baseline")));
        archive.Root.AddFile(Bml("track.bml", tracks));
        BinaryXmlNode trackLocale = new("trackList");
        trackLocale.Children.Add(Node("track", ("id", "baseline"), ("name", "Baseline")));
        trackLocale.Children.Add(Node("track_rvs", ("refId", "baseline")));
        archive.Root.AddFile(Bml("trackLocale@kr.bml", trackLocale));
    }

    private static void AddDonorTrackCommon(RhoArchiveDocument archive)
    {
        BinaryXmlNode challenge = new("challengeList");
        for (int mission = 0x41; mission <= 0x46; mission++)
        {
            BinaryXmlNode row = Node(
                "challenge",
                ("id", $"0x{mission:X2}"),
                ("type", "4"),
                ("icon", mission == 0x46 ? "라이센스1타임어택" : $"라이센스1합성{mission:X2}"),
                ("require", $"0x{mission - 1:X2}"),
                ("track", "synthetic"),
                ("time", "1000"),
                ("oneTime", "true"),
                ("isGetLicense", "true"),
                ("secondPerLevel", "1000"));
            if (mission == 0x42)
            {
                row.SetAttribute("loadItem", "waterbomb,angel,rocket");
                row.SetAttribute("cubeItem", "waterbomb,angel,rocket");
            }
            else if (mission == 0x44)
            {
                row.SetAttribute("arrow", "true");
                row.SetAttribute("disappearArrowIdx", "10");
            }

            challenge.Children.Add(row);
        }

        archive.Root.AddFile(Bml("challenge.bml", challenge));
        BinaryXmlNode locale = new("challengeList");
        for (int mission = 0x41; mission <= 0x46; mission++)
        {
            locale.Children.Add(Node(
                "challenge",
                ("id", $"0x{mission:X2}"),
                ("name", $"Synthetic {mission:X2}"),
                ("rewardLicense1", "1")));
        }

        archive.Root.AddFile(Bml("challengeLocale@kr.bml", locale));
        BinaryXmlNode tracks = new("trackList");
        BinaryXmlNode trackLocale = new("trackList");
        foreach (string track in CatalogTracks)
        {
            tracks.Children.Add(Node("track", ("id", track), ("laps", "1")));
            trackLocale.Children.Add(Node("track", ("id", track), ("name", track)));
        }

        foreach (string track in TrackReverse)
        {
            tracks.Children.Add(Node("track_rvs", ("refId", track), ("laps", "1")));
        }

        foreach (string track in TrackLocaleReverse)
        {
            trackLocale.Children.Add(Node("track_rvs", ("refId", track), ("name", track)));
        }

        archive.Root.AddFile(Bml("track.bml", tracks));
        archive.Root.AddFile(Bml("trackLocale@kr.bml", trackLocale));
    }

    private static void AddDonorChallengeReady(RhoArchiveDocument archive)
    {
        for (int mission = 0x41; mission <= 0x46; mission++)
        {
            string id = mission.ToString("X2", CultureInfo.InvariantCulture);
            RhoArchiveFolder folder = archive.Root.AddFolder(id);
            string map = $"L1_{id}_track";
            string first = $"L1_{id}_01";
            string second = $"L1_{id}_02";
            string third = $"L1_{id}_03";
            bool hasThird = mission is 0x43 or 0x45 or 0x46;
            BinaryXmlNode description = new("Container");
            description.Children.Add(Node("Panel", ("texture", map)));
            description.Children.Add(Node("Panel", ("texture", first)));
            description.Children.Add(Node("Panel", ("texture", second)));
            if (hasThird)
            {
                description.Children.Add(Node("Panel", ("texture", third)));
            }
            BinaryXmlNode bag = new("StringBag");
            for (int index = 1; index <= 2; index++)
            {
                BinaryXmlNode key = Node("k", ("n", $"desc{index}"));
                key.Children.Add(Node("m", ("c", "kr"), ("v", $"합성 설명 {id}-{index}")));
                bag.Children.Add(key);
            }

            description.Children.Add(bag);
            folder.AddFile(Bml("descript.bml", description));
            folder.AddFile(ArchiveFile(map + ".png", map));
            folder.AddFile(ArchiveFile(first + ".jpg", first));
            folder.AddFile(ArchiveFile(second + ".jpg", second));
            if (hasThird)
            {
                folder.AddFile(ArchiveFile(third + ".jpg", third));
            }
        }
    }

    private static void AddDonorStageCommon(RhoArchiveDocument archive)
    {
        RhoArchiveFolder icon = archive.Root.AddFolder("icon");
        foreach (string category in new[] { "고가", "다운힐", "대마왕", "로두마니", "타임어택" })
        {
            for (int state = 1; state <= 4; state++)
            {
                string name = $"라이센스1{category}_{state}.png";
                icon.AddFile(ArchiveFile(name, name));
            }
        }

        icon.AddFile(ArchiveFile("라이센스1자격심사_1.png", "excluded"));
    }

    private static RhoArchiveMetadata SaveArchive(
        string directory,
        string name,
        Action<RhoArchiveDocument> populate)
    {
        RhoArchiveDocument archive = RhoArchiveDocument.Create(layerVersion: 1);
        populate(archive);
        return archive.Save(Path.Combine(directory, name));
    }

    private static void SaveMetadata(
        string path,
        IReadOnlyDictionary<string, RhoArchiveMetadata> entries)
    {
        BinaryXmlNode root = Node("PackFolder", ("name", "KartRider"));
        BinaryXmlNode trackFolder = Node("PackFolder", ("name", "track"));
        root.Children.Add(trackFolder);
        foreach ((string name, RhoArchiveMetadata metadata) in entries)
        {
            BinaryXmlNode entry = Node(
                "RhoFolder",
                ("name", Path.GetFileNameWithoutExtension(name)),
                ("fileName", name),
                ("key", metadata.Key.ToString(CultureInfo.InvariantCulture)),
                ("dataHash", metadata.DataHash.ToString(CultureInfo.InvariantCulture)),
                ("mediaSize", metadata.MediaSize.ToString(CultureInfo.InvariantCulture)));
            trackFolder.Children.Add(entry);
        }

        AaaMetadataDocument.Create(root).Save(path);
    }

    private static RhoArchiveFile Bml(string name, BinaryXmlNode root) =>
        new(name, BinaryXmlCodec.Write(root), RhoFileProperty.CompressedEncrypted);

    private static RhoArchiveFile ArchiveFile(string name, string value) =>
        new(name, Encoding.UTF8.GetBytes(value), RhoFileProperty.None);

    private static BinaryXmlNode Node(
        string name,
        params (string Name, string Value)[] attributes)
    {
        BinaryXmlNode node = new(name);
        foreach ((string attributeName, string value) in attributes)
        {
            node.SetAttribute(attributeName, value);
        }

        return node;
    }

    private static BinaryXmlNode ReadBml(RhoArchiveDocument archive, string path)
    {
        RhoArchiveFile? file = archive.Root.GetFile(path);
        Assert.NotNull(file);
        return BinaryXmlCodec.Read(file.Data);
    }

    private static string Hash(string path)
    {
        using FileStream stream = System.IO.File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static SortedDictionary<string, string> SnapshotTree(string root)
    {
        SortedDictionary<string, string> snapshot = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in Directory.EnumerateDirectories(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            snapshot[Path.GetRelativePath(root, directory)] = "directory";
        }

        foreach (string file in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            snapshot[Path.GetRelativePath(root, file)] = $"file:{Hash(file)}";
        }

        return snapshot;
    }
}
