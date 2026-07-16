using System.Globalization;
using System.Security.Cryptography;
using KartRider.P236.ItemProbabilities.Internal;

namespace KartRider.P236.ItemProbabilities;

public sealed record P236L1DataPatchResult(
    bool Changed,
    int ChangedFileCount,
    int GeneratedArchiveCount,
    int AddedTrackArchiveCount,
    string TargetDataDirectory,
    string? BackupDirectory);

/// <summary>
/// Builds the P236 L1-compatible client data locally from a user-supplied
/// later KartRider client. No RHO, BML, texture, or track payload is embedded
/// in this assembly.
/// </summary>
public static class P236L1DataPatcher
{
    public const int RecipeVersion = 1;

    private static readonly string[] ExistingArchiveNames =
    [
        "sound_bgm_factory.rho",
        "sound_fx_surround.rho",
        "stage_challengeReady.rho",
        "stage_common.rho",
        "theme_common.rho",
        "theme_factory.rho",
        "theme_forest.rho",
        "theme_ice.rho",
        "theme_northeu.rho",
        "theme_village.rho",
        "track_common.rho",
        "zeta_kr_content.rho",
    ];

    private static readonly string[] AddedTrackArchiveNames =
    [
        "track_factory_I05.rho",
        "track_factory_I06.rho",
        "track_factory_I07.rho",
        "track_factory_R01.rho",
        "track_factory_R02.rho",
        "track_forest_F01.rho",
        "track_forest_I05.rho",
        "track_forest_I06.rho",
        "track_forest_I07.rho",
        "track_forest_R04.rho",
        "track_forest_R05.rho",
        "track_ice_C001.rho",
        "track_northeu_I05.rho",
        "track_northeu_I06.rho",
        "track_northeu_I07.rho",
        "track_northeu_I08.rho",
        "track_northeu_I09.rho",
        "track_northeu_R01.rho",
        "track_northeu_R02.rho",
        "track_village_C114.rho",
        "track_village_C115.rho",
        "track_village_C116.rho",
    ];

    private static readonly string[] CatalogTrackIds =
    [
        "northeu_I05",
        "northeu_I06",
        "northeu_I07",
        "northeu_I08",
        "northeu_I09",
        "northeu_R01",
        "northeu_R02",
        "factory_I05",
        "factory_I06",
        "factory_I07",
        "factory_R01",
        "factory_R02",
        "forest_F01",
        "forest_I05",
        "forest_I06",
        "forest_I07",
        "forest_R04",
        "forest_R05",
    ];

    private static readonly string[] TrackReverseIds =
    [
        "northeu_I05",
        "northeu_I06",
        "northeu_I07",
    ];

    private static readonly string[] TrackLocaleReverseIds =
    [
        "northeu_I05",
        "northeu_I06",
        "northeu_I07",
        "northeu_R01",
        "factory_R01",
        "forest_I05",
        "forest_I06",
        "forest_R04",
    ];

    private static readonly string[] L1IconNodeNames = Enumerable.Range(1, 4)
        .SelectMany(suffix => new[] { "고가", "다운힐", "대마왕", "로두마니", "타임어택" }
            .Select(category => $"라이센스1{category}_{suffix}"))
        .ToArray();

    private static readonly string[] SoundFxEntries =
    [
        "FM컴퓨터2.ogg",
        "FM컴퓨터3.ogg",
        "mo_금속삐그덕.ogg",
        "mo_밧줄.ogg",
        "mo_트램.ogg",
        "강물2.ogg",
        "경기장.ogg",
        "경기장2.ogg",
        "공장컨베어04.ogg",
        "나뭇잎바람.ogg",
        "노르테유_기차.ogg",
        "새소리바람.ogg",
        "숲속새.ogg",
        "우주선점프대.ogg",
        "터널02.ogg",
        "팬더.ogg",
    ];

    private static readonly string[] ThemeArchiveNames =
    [
        "theme_common.rho",
        "theme_factory.rho",
        "theme_forest.rho",
        "theme_ice.rho",
        "theme_northeu.rho",
        "theme_village.rho",
    ];

    private static readonly IReadOnlyDictionary<string, ThemeFingerprint> ThemeFingerprints =
        new Dictionary<string, ThemeFingerprint>(StringComparer.OrdinalIgnoreCase)
        {
            ["theme_common.rho"] = new(
                209,
                [
                    "texture/G_Sign.dds",
                    "texture/sign-danger-1.png",
                    "texture/t_sign_ani_g.dds",
                ]),
            ["theme_factory.rho"] = new(49, []),
            ["theme_forest.rho"] = new(43, []),
            ["theme_ice.rho"] = new(
                80,
                [
                    "texture/fence_xmasA.dds",
                    "texture/fence_xmasB.dds",
                    "texture/sing_ice_CA.png",
                    "texture/sing_ice_CA_1.png",
                ]),
            ["theme_northeu.rho"] = new(117, []),
            ["theme_village.rho"] = new(73, []),
        };

    private static readonly IReadOnlyDictionary<string, string> DonorSha256 =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["aaa.pk"] = "ACB99B97A7BBE51AB71F3BE2942F09DC25701BBF6ECC6EC37D79B6ABA7E46765",
            ["sound_bgm_factory.rho"] = "3F1DACCB4476DB455D9ADE4804865FD7836188A639DA0B360E5404DF12E9ACD0",
            ["sound_fx_surround.rho"] = "FA8D9AEF86089C041A82CCA135E8EAC8E872160337809AF342E72233744295A5",
            ["stage_challengeReady.rho"] = "2F2FC34F1767D0F73012E2F09116979CB27E37E7EA0351610F7B744D8793CF33",
            ["stage_common.rho"] = "C39F3E37FC0E4E33F7AFBB10B4651F3041E3FC07097D15FE419EC5564D16BA0D",
            ["theme_common.rho"] = "CF42CAA38B452BB4F93C51D6C75F53260CE2137A461E006E4F8E7BD160F29C43",
            ["theme_factory.rho"] = "4091C125A19F539237FADEA5AF460E63C877023990D6F8009CA3B19C23CAAA71",
            ["theme_forest.rho"] = "3AB11635D98FCD425261002867967B307196871B9F30CC6D322DEF05CBBD04EE",
            ["theme_ice.rho"] = "2683DB5B48688C3B534DF98FB1BB7C407BC0B8BDAAE6E6CC5D4F37846735E04A",
            ["theme_northeu.rho"] = "EC6ABAC60B5A390990C5889282D77624DBACA28D621B8770AB4A24EA26CFCC30",
            ["theme_village.rho"] = "6C75680200EEF30151F8BD3619DD24010C63610C16E5A0B37C4DEAF55CAE2325",
            ["track_common.rho"] = "50E33EF2768C3E8CA82991DA3A550181B39828AB6244B8224B981164650711F9",
            ["track_forest_B01.rho"] = "4DB1D48103DCF4592F4D230B6D66474B1365103D67B0801705F41C38F92946E3",
            ["zeta_kr_content.rho"] = "048D61B2D09E8CA3A8EF7C9B1121762763BB222059B5F82A28956DEAFA002D15",
            ["track_factory_I05.rho"] = "7EAFD1A1F37DCBD108F770DF20558A23F87679E565045C8B748830F3FC91CDDA",
            ["track_factory_I06.rho"] = "5CD930A4226FEF51FD2482B1C5E8221BD79CFD79598D6DCDA8A074ABD7C1DDCD",
            ["track_factory_I07.rho"] = "37F4B89741295B408D5C4B2B67DD9C3E1F4E9FB457E1B8C3D9E0FA29B142A274",
            ["track_factory_R01.rho"] = "E5C7407A2DBB178787A4B5638E987B37C39A0DC6B51F76ABDEBBB6A514791BC2",
            ["track_factory_R02.rho"] = "6B7839C410202290F48F1BCE1B2FD2E59857C812BE1441F0EBA1809D792B6A8D",
            ["track_forest_F01.rho"] = "66A1CE73A78F803BF4836B4E37437D6C6E7E10BF9B5CFDC025D7B6F5FEA692E3",
            ["track_forest_I05.rho"] = "1CA6A7659E4D74128013B23BE4EF7A3ED2EC6F93C5BFFFB08749974BD242EF9F",
            ["track_forest_I06.rho"] = "D5EC048019D1A0D7F35ED9237EF484136F02C1737D4E31B3F24713B845F18713",
            ["track_forest_I07.rho"] = "EACDB244FE95EB4788693B8F63AC5C4F2F8161FAC0534FBE0EBDAD8BD78EDD35",
            ["track_forest_R04.rho"] = "B5CD8A23FD746E670F1739F8D7973E3C4FA4254EB0874D78C06E50ED1BB55E6A",
            ["track_forest_R05.rho"] = "92ABB007A7F8DC6F572E071BF9E6DA50908B574DFD66402A6357304523EA449D",
            ["track_ice_C001.rho"] = "E82208A464B47E276518DFA10989490B6F6C6845B4347221295B97A86B81A17B",
            ["track_northeu_I05.rho"] = "69CCD99212347BFFC19CC9A92CC9E077DE0EA6BB3E080475475EF3BE654CDCBB",
            ["track_northeu_I06.rho"] = "803141A65D9BEF2ADF1AA29E058384A397392D425DEEA1C70F180E87F5BC175A",
            ["track_northeu_I07.rho"] = "B4B1F5D46821671C8B66EC2AA6F5E7D7968322AF33CCFD82F916157F8A5E71AB",
            ["track_northeu_I08.rho"] = "EE29BC6ABF17BC35A9738E455D70D86D46EC7FB57FE4BF11EE5108917841C25D",
            ["track_northeu_I09.rho"] = "6C2897983EFE3A71075A568AAE621FB11AE4EF62FE2E5E2F501DF4166D361905",
            ["track_northeu_R01.rho"] = "91657D82F44F95BA9FCD91A06EA6798BA809A3FE7F1078043DB60695FB58CEC9",
            ["track_northeu_R02.rho"] = "3C5AFFEF25766DD59888F5D435ADA834ED6A8D62C0265DB76A9282FA0BC49927",
            ["track_village_C114.rho"] = "5419B085E85EDF3767A06F549A679C7FE38D59CD17C3DC7653C9DC16EFDD637C",
            ["track_village_C115.rho"] = "830EAA7C56A275CA67B45D230EAA1F0F0A0D02A6EADA49B238837A1C5580BCF0",
            ["track_village_C116.rho"] = "9DC03D4261A204FF6C7A9ED80AC2CD956D07354EE2D6F2523B90B1A5CCC44097",
        };

    private static readonly HashSet<string> CatalogTrackIdSet =
        new(CatalogTrackIds, StringComparer.OrdinalIgnoreCase);

    public static P236L1DataPatchResult Apply(
        string donorClientOrDataDirectory,
        string targetClientOrDataDirectory,
        IProgress<string>? progress = null)
    {
        return ApplyCore(
            donorClientOrDataDirectory,
            targetClientOrDataDirectory,
            progress,
            validateDonorFingerprint: true);
    }

    internal static P236L1DataPatchResult ApplyForTesting(
        string donorClientOrDataDirectory,
        string targetClientOrDataDirectory,
        IProgress<string>? progress = null)
    {
        return ApplyCore(
            donorClientOrDataDirectory,
            targetClientOrDataDirectory,
            progress,
            validateDonorFingerprint: false);
    }

    private static P236L1DataPatchResult ApplyCore(
        string donorClientOrDataDirectory,
        string targetClientOrDataDirectory,
        IProgress<string>? progress,
        bool validateDonorFingerprint)
    {
        string donorData = ResolveDataDirectory(donorClientOrDataDirectory, "donor");
        string targetData = ResolveDataDirectory(targetClientOrDataDirectory, "target");
        if (string.Equals(donorData, targetData, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Donor와 패치 대상 Data 폴더는 서로 달라야 합니다.");
        }

        string donorSnapshotDirectory = Path.Combine(
            Path.GetTempPath(),
            $".p236-l1-donor-snapshot-{Guid.NewGuid():N}");
        try
        {
            CreateDonorSnapshot(
                donorData,
                donorSnapshotDirectory,
                validateDonorFingerprint);

            using P236L1DataTransaction transaction = P236L1DataTransaction.Acquire(targetData);
            RefusePendingItemProbabilityTransaction(targetData);
            if (transaction.RecoverIfNeeded())
            {
                progress?.Report("중단된 이전 L1 데이터 작업을 복구했습니다.");
            }

            ValidateInputs(donorSnapshotDirectory, targetData);
            string buildDirectory = Path.Combine(
                targetData,
                $".p236-l1-data-build-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(buildDirectory);
                progress?.Report("donor RHO를 읽고 P236 호환 데이터를 생성하는 중...");
                IReadOnlyDictionary<string, string> replacements = BuildReplacements(
                    donorSnapshotDirectory,
                    targetData,
                    buildDirectory,
                    progress);

                P236L1DataCommitResult commit = transaction.Commit(
                    replacements,
                    RecipeVersion,
                    progress);
                return new P236L1DataPatchResult(
                    commit.ChangedFileCount != 0,
                    commit.ChangedFileCount,
                    ExistingArchiveNames.Length + AddedTrackArchiveNames.Length,
                    AddedTrackArchiveNames.Length,
                    targetData,
                    commit.BackupDirectory);
            }
            finally
            {
                TryDeleteBuildDirectory(buildDirectory);
            }
        }
        finally
        {
            TryDeleteDonorSnapshotDirectory(donorSnapshotDirectory);
        }
    }

    public static P236L1DataPatchResult Restore(
        string targetClientOrDataDirectory,
        IProgress<string>? progress = null)
    {
        string targetData = ResolveDataDirectory(targetClientOrDataDirectory, "target");
        using P236L1DataTransaction transaction = P236L1DataTransaction.Acquire(targetData);
        RefusePendingItemProbabilityTransaction(targetData);
        if (transaction.RecoverIfNeeded())
        {
            progress?.Report("중단된 이전 L1 데이터 작업을 복구했습니다.");
        }

        P236L1DataCommitResult restored = transaction.Restore(progress);
        return new P236L1DataPatchResult(
            restored.ChangedFileCount != 0,
            restored.ChangedFileCount,
            ExistingArchiveNames.Length + AddedTrackArchiveNames.Length,
            AddedTrackArchiveNames.Length,
            targetData,
            restored.BackupDirectory);
    }

    internal static string ResolveDataDirectory(string path, string role)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{role} 경로가 비어 있습니다.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path.Trim());
        if (File.Exists(fullPath))
        {
            if (!string.Equals(Path.GetFileName(fullPath), "aaa.pk", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{role} 파일 경로는 aaa.pk여야 합니다: {fullPath}");
            }

            fullPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"{role} aaa.pk의 상위 폴더가 없습니다.");
        }
        else if (Directory.Exists(Path.Combine(fullPath, "Data")) &&
                 File.Exists(Path.Combine(fullPath, "Data", "aaa.pk")))
        {
            fullPath = Path.Combine(fullPath, "Data");
        }

        if (!Directory.Exists(fullPath) || !File.Exists(Path.Combine(fullPath, "aaa.pk")))
        {
            throw new DirectoryNotFoundException(
                $"{role} Data 폴더 또는 클라이언트 루트를 찾을 수 없습니다: {fullPath}");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
    }

    private static void RefusePendingItemProbabilityTransaction(string targetData)
    {
        string markerPath = Path.Combine(
            targetData,
            P236ItemProbabilityTransaction.MarkerFileName);
        if (File.Exists(markerPath))
        {
            throw new InvalidOperationException(
                "아이템 확률 데이터 작업의 복구가 대기 중입니다. " +
                "아이템 확률 편집기에서 복구를 먼저 완료하세요.");
        }
    }

    private static void CreateDonorSnapshot(
        string donorData,
        string snapshotDirectory,
        bool validateDonorFingerprint)
    {
        ValidateDonorFingerprintDefinition();
        Directory.CreateDirectory(snapshotDirectory);
        foreach ((string fileName, string expectedHash) in DonorSha256)
        {
            RequireFile(donorData, fileName, "donor");
            string sourcePath = Path.Combine(donorData, fileName);
            string snapshotPath = Path.Combine(snapshotDirectory, fileName);
            using (FileStream source = new(
                       sourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            using (FileStream destination = new(
                       snapshotPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            if (validateDonorFingerprint)
            {
                string actualHash = ComputeSha256(snapshotPath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"지원하는 L1 donor 원본과 일치하지 않는 파일입니다: {fileName}. " +
                        "지원 대상인 원본 L1 donor 클라이언트를 선택해 주세요.");
                }
            }
        }
    }

    private static void ValidateDonorFingerprintDefinition()
    {
        HashSet<string> requiredFiles = new(
            ExistingArchiveNames
                .Concat(AddedTrackArchiveNames)
                .Append("track_forest_B01.rho")
                .Append("aaa.pk"),
            StringComparer.OrdinalIgnoreCase);
        if (!requiredFiles.SetEquals(DonorSha256.Keys))
        {
            throw new InvalidOperationException(
                "L1 donor recipe 파일 집합과 SHA-256 fingerprint 정의가 일치하지 않습니다.");
        }
    }

    private static void ValidateInputs(string donorData, string targetData)
    {
        foreach (string archiveName in ExistingArchiveNames)
        {
            RequireFile(targetData, archiveName, "P236 target");
            RequireFile(donorData, archiveName, "donor");
        }

        foreach (string archiveName in AddedTrackArchiveNames)
        {
            RequireFile(donorData, archiveName, "donor");
        }

        RequireFile(donorData, "track_forest_B01.rho", "donor");

        // Semantic checks supplement the exact public donor fingerprint and
        // also keep synthetic recipe tests fail-closed.
        RhoArchiveDocument targetTrack = RhoArchiveDocument.Open(
            Path.Combine(targetData, "track_common.rho"));
        BinaryXmlNode targetChallenges = ReadBml(targetTrack, "challenge.bml");
        RequireUniqueChildByAttribute(targetChallenges, "challenge", "id", "0x34");

        RhoArchiveDocument donorTrack = RhoArchiveDocument.Open(
            Path.Combine(donorData, "track_common.rho"));
        BinaryXmlNode donorChallenges = ReadBml(donorTrack, "challenge.bml");
        foreach (int mission in Enumerable.Range(0x41, 6))
        {
            RequireUniqueChildByAttribute(
                donorChallenges,
                "challenge",
                "id",
                $"0x{mission:X2}");
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static IReadOnlyDictionary<string, string> BuildReplacements(
        string donorData,
        string targetData,
        string buildDirectory,
        IProgress<string>? progress)
    {
        Dictionary<string, string> replacements =
            new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, RhoArchiveMetadata> metadata =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (string archiveName in ExistingArchiveNames)
        {
            progress?.Report($"L1 데이터 생성: {archiveName}");
            string outputPath = Path.Combine(buildDirectory, archiveName);
            RhoArchiveDocument target = RhoArchiveDocument.Open(
                Path.Combine(targetData, archiveName));

            switch (archiveName)
            {
                case "sound_bgm_factory.rho":
                    RhoArchiveDocument donorBgm = OpenDonor(donorData, archiveName);
                    ValidateMissingOnlyFingerprint(
                        archiveName,
                        target.Root,
                        donorBgm.Root,
                        ["BGM-공장03.ogg"]);
                    MergeMissingTree(
                        target.Root,
                        donorBgm.Root);
                    break;
                case "sound_fx_surround.rho":
                    MergeSelectedRootFiles(
                        target.Root,
                        OpenDonor(donorData, archiveName).Root,
                        SoundFxEntries);
                    SortFilesByNameWithoutExtension(target.Root);
                    break;
                case "stage_challengeReady.rho":
                    PatchChallengeReady(
                        target,
                        OpenDonor(donorData, archiveName));
                    break;
                case "stage_common.rho":
                    PatchStageCommon(
                        target,
                        OpenDonor(donorData, archiveName));
                    break;
                case "track_common.rho":
                    PatchTrackCommon(
                        target,
                        OpenDonor(donorData, archiveName));
                    break;
                case "zeta_kr_content.rho":
                    PatchMaximumLicenseLevel(target);
                    break;
                default:
                    if (!ThemeArchiveNames.Contains(archiveName, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"No L1 data recipe exists for {archiveName}.");
                    }

                    RhoArchiveDocument donorTheme = OpenDonor(donorData, archiveName);
                    ValidateThemeFingerprint(
                        archiveName,
                        target.Root,
                        donorTheme.Root);
                    MergeMissingTree(
                        target.Root,
                        donorTheme.Root);
                    if (string.Equals(
                            archiveName,
                            "theme_forest.rho",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        AddForestFloorTexture(target, donorData);
                    }

                    SortFilesByNameWithoutExtension(
                        RequireFolder(target.Root, "texture"));
                    break;
            }

            RhoArchiveMetadata saved = target.Save(outputPath);
            RhoArchiveDocument reopened = RhoArchiveDocument.Open(outputPath);
            target.ValidateSemanticallyEqual(reopened);
            replacements[archiveName] = outputPath;
            metadata[archiveName] = saved;
        }

        foreach (string archiveName in AddedTrackArchiveNames)
        {
            string donorPath = Path.Combine(donorData, archiveName);
            string targetPath = Path.Combine(targetData, archiveName);
            if (File.Exists(targetPath) && !FilesEqual(donorPath, targetPath))
            {
                throw new InvalidDataException(
                    $"대상에 donor와 다른 트랙 RHO가 이미 있어 덮어쓰지 않았습니다: {archiveName}");
            }

            string outputPath = Path.Combine(buildDirectory, archiveName);
            File.Copy(donorPath, outputPath, overwrite: false);
            _ = RhoArchiveDocument.Open(outputPath);
            replacements[archiveName] = outputPath;
            metadata[archiveName] = RhoArchiveDocument.ReadMetadata(outputPath);
        }

        string targetMetadataPath = Path.Combine(targetData, "aaa.pk");
        AaaMetadataDocument targetMetadata = AaaMetadataDocument.Open(targetMetadataPath);
        AaaMetadataDocument donorMetadata = AaaMetadataDocument.Open(
            Path.Combine(donorData, "aaa.pk"));
        foreach (string archiveName in ExistingArchiveNames.Concat(AddedTrackArchiveNames))
        {
            targetMetadata.EnsureArchiveEntryFrom(donorMetadata, archiveName);
            targetMetadata.UpdateArchive(archiveName, metadata[archiveName]);
        }

        targetMetadata.SortArchiveEntriesInPackFolder("track");

        string outputMetadataPath = Path.Combine(buildDirectory, "aaa.pk");
        targetMetadata.Save(outputMetadataPath);
        AaaMetadataDocument reopenedMetadata = AaaMetadataDocument.Open(outputMetadataPath);
        targetMetadata.ValidateSemanticallyEqual(reopenedMetadata);
        foreach (string archiveName in ExistingArchiveNames.Concat(AddedTrackArchiveNames))
        {
            reopenedMetadata.ValidateArchive(archiveName, metadata[archiveName]);
        }

        replacements["aaa.pk"] = outputMetadataPath;
        return replacements;
    }

    private static RhoArchiveDocument OpenDonor(string donorData, string archiveName) =>
        RhoArchiveDocument.Open(Path.Combine(donorData, archiveName));

    private static void PatchTrackCommon(
        RhoArchiveDocument target,
        RhoArchiveDocument donor)
    {
        PatchChallengeList(target, donor);
        PatchChallengeLocale(target, donor);
        PatchTrackCatalog(target, donor, "track.bml");
        PatchTrackCatalog(target, donor, "trackLocale@kr.bml");
    }

    private static void PatchChallengeList(
        RhoArchiveDocument target,
        RhoArchiveDocument donor)
    {
        RhoArchiveFile targetFile = RequireArchiveFile(target.Root, "challenge.bml");
        BinaryXmlNode targetRoot = BinaryXmlCodec.Read(targetFile.Data);
        BinaryXmlNode donorRoot = ReadBml(donor, "challenge.bml");
        targetRoot.Children.RemoveAll(child => IsL1MissionNode(child, includeJudgment: true));

        foreach (int mission in Enumerable.Range(0x41, 6))
        {
            string missionId = $"0x{mission:X2}";
            BinaryXmlNode row = CloneNode(
                RequireUniqueChildByAttribute(
                    donorRoot,
                    "challenge",
                    "id",
                    missionId));
            row.SetAttribute("type", "3");
            row.SetAttribute("lap", "1");
            RemoveAttributes(
                row,
                "oneTime",
                "isGetLicense",
                "secondPerLevel",
                "arrow",
                "disappearArrowIdx");
            if (mission == 0x41)
            {
                row.SetAttribute("require", "0x34");
            }
            else if (mission == 0x42)
            {
                row.SetAttribute("loadItem", "rocket");
                row.SetAttribute("cubeItem", "rocket");
            }
            else if (mission == 0x44)
            {
                row.SetAttribute("showArrow", "true");
            }

            MoveAttributeAfter(row, "lap", "track");
            if (mission == 0x44)
            {
                MoveAttributeAfter(row, "showArrow", "time");
            }

            targetRoot.Children.Add(row);
        }

        targetFile.Data = BinaryXmlCodec.Write(targetRoot);
    }

    private static void PatchChallengeLocale(
        RhoArchiveDocument target,
        RhoArchiveDocument donor)
    {
        const string path = "challengeLocale@kr.bml";
        RhoArchiveFile targetFile = RequireArchiveFile(target.Root, path);
        BinaryXmlNode targetRoot = BinaryXmlCodec.Read(targetFile.Data);
        BinaryXmlNode donorRoot = ReadBml(donor, path);
        targetRoot.Children.RemoveAll(child => IsL1MissionNode(child, includeJudgment: true));
        targetRoot.Children.RemoveAll(child =>
            string.Equals(child.Name, "passMsg", StringComparison.OrdinalIgnoreCase) &&
            child.GetAttribute("k") is "4_yet" or "4_get");
        int firstChallengeIndex = targetRoot.Children.FindIndex(child =>
            string.Equals(child.Name, "challenge", StringComparison.OrdinalIgnoreCase));
        if (firstChallengeIndex < 0)
        {
            firstChallengeIndex = targetRoot.Children.Count;
        }

        targetRoot.Children.Insert(
            firstChallengeIndex,
            Node(
                "passMsg",
                ("k", "4_yet"),
                ("v", "성공하셨습니다! 새로운 L1 라이센스 코스에 도전할 수 있습니다.")));
        targetRoot.Children.Insert(
            firstChallengeIndex + 1,
            Node(
                "passMsg",
                ("k", "4_get"),
                ("v", "성공하셨습니다! L1 라이센스 코스를 모두 완료하셨습니다.")));
        foreach (int mission in Enumerable.Range(0x41, 6))
        {
            BinaryXmlNode row = CloneNode(
                RequireUniqueChildByAttribute(
                    donorRoot,
                    "challenge",
                    "id",
                    $"0x{mission:X2}"));
            RemoveAttributes(
                row,
                "rewardLicense1",
                "rewardLicense2",
                "rewardLicense3",
                "gift1",
                "gift2",
                "gift3");
            targetRoot.Children.Add(row);
        }

        targetFile.Data = BinaryXmlCodec.Write(targetRoot);
    }

    private static void PatchTrackCatalog(
        RhoArchiveDocument target,
        RhoArchiveDocument donor,
        string path)
    {
        RhoArchiveFile targetFile = RequireArchiveFile(target.Root, path);
        BinaryXmlNode targetRoot = BinaryXmlCodec.Read(targetFile.Data);
        BinaryXmlNode donorRoot = ReadBml(donor, path);
        string[] reverseIds = string.Equals(
                path,
                "track.bml",
                StringComparison.OrdinalIgnoreCase)
            ? TrackReverseIds
            : TrackLocaleReverseIds;
        int selectedDonorRows = donorRoot.Children.Count(IsCatalogTrackNode);
        int expectedDonorRows = CatalogTrackIds.Length + reverseIds.Length;
        if (selectedDonorRows != expectedDonorRows)
        {
            throw new InvalidDataException(
                $"Donor {path} contains {selectedDonorRows} selected catalog rows; " +
                $"expected {expectedDonorRows}.");
        }

        targetRoot.Children.RemoveAll(IsCatalogTrackNode);

        List<BinaryXmlNode> normalRows = CatalogTrackIds
            .Select(trackId => CloneNode(RequireCatalogRow(
                donorRoot,
                "track",
                trackId)))
            .ToList();
        int reverseAnchor = targetRoot.Children.FindIndex(child =>
            string.Equals(child.Name, "track_rvs", StringComparison.OrdinalIgnoreCase));
        if (reverseAnchor < 0)
        {
            reverseAnchor = targetRoot.Children.Count;
        }

        targetRoot.Children.InsertRange(reverseAnchor, normalRows);
        foreach (string trackId in reverseIds)
        {
            targetRoot.Children.Add(CloneNode(RequireCatalogRow(
                donorRoot,
                "track_rvs",
                trackId)));
        }

        targetFile.Data = BinaryXmlCodec.Write(targetRoot);
    }

    private static bool IsCatalogTrackNode(BinaryXmlNode node)
    {
        string? trackId = GetCatalogTrackId(node);
        return trackId is not null &&
               (string.Equals(node.Name, "track", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Name, "track_rvs", StringComparison.OrdinalIgnoreCase)) &&
               CatalogTrackIdSet.Contains(trackId);
    }

    private static BinaryXmlNode RequireCatalogRow(
        BinaryXmlNode root,
        string nodeName,
        string trackId)
    {
        List<BinaryXmlNode> matches = root.Children.Where(child =>
                string.Equals(child.Name, nodeName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    GetCatalogTrackId(child),
                    trackId,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"Donor catalog must contain exactly one {nodeName} row for '{trackId}'.");
        }

        return matches[0];
    }

    private static string? GetCatalogTrackId(BinaryXmlNode node) =>
        string.Equals(node.Name, "track_rvs", StringComparison.OrdinalIgnoreCase)
            ? node.GetAttribute("refId") ?? node.GetAttribute("id")
            : node.GetAttribute("id") ?? node.GetAttribute("refId");

    private static bool IsL1MissionNode(BinaryXmlNode node, bool includeJudgment)
    {
        string? id = node.GetAttribute("id");
        if (id is null || !id.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(id.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
        {
            return false;
        }

        return value is >= 0x41 and <= 0x46 || includeJudgment && value == 0x40;
    }

    private static void PatchMaximumLicenseLevel(RhoArchiveDocument target)
    {
        RhoArchiveFile file = RequireArchiveFile(target.Root, "config.bml");
        BinaryXmlNode root = BinaryXmlCodec.Read(file.Data);
        List<BinaryXmlNode> matches = BinaryXmlCodec.Enumerate(root)
            .Where(node =>
                string.Equals(node.Name, "content", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    node.GetAttribute("name"),
                    "maxLicenseLv",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                "P236 zeta config must contain exactly one maxLicenseLv entry.");
        }

        matches[0].SetAttribute("value", "4");
        file.Data = BinaryXmlCodec.Write(root);
    }

    private static void PatchStageCommon(
        RhoArchiveDocument target,
        RhoArchiveDocument donor)
    {
        RhoArchiveFolder donorIcon = RequireFolder(donor.Root, "icon");
        RhoArchiveFolder targetIcon = GetOrAddFolder(target.Root, "icon");
        HashSet<string> iconNameSet = new(
            L1IconNodeNames,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, RhoArchiveFile> donorByNodeName = donorIcon.Files
            .Where(file => iconNameSet.Contains(file.NameWithoutExtension))
            .ToDictionary(
                file => file.NameWithoutExtension,
                StringComparer.OrdinalIgnoreCase);
        if (donorByNodeName.Count != L1IconNodeNames.Length)
        {
            throw new InvalidDataException(
                $"Donor stage_common contains {donorByNodeName.Count} compatible L1 icons; " +
                $"expected {L1IconNodeNames.Length}.");
        }

        targetIcon.Files.RemoveAll(file => iconNameSet.Contains(file.NameWithoutExtension));
        foreach (string nodeName in L1IconNodeNames)
        {
            targetIcon.AddFile(CloneFile(donorByNodeName[nodeName]));
        }

        RhoArchiveFile iconBml = RequireArchiveFile(target.Root, "icon/icon.bml");
        BinaryXmlNode iconRoot = BinaryXmlCodec.Read(iconBml.Data);
        int menuNodeCount = iconRoot.Children.Count(child =>
            string.Equals(child.Name, "메뉴라이센스1", StringComparison.Ordinal));
        if (menuNodeCount != 1)
        {
            throw new InvalidDataException(
                "P236 stage_common icon catalog must contain exactly one 메뉴라이센스1 node.");
        }

        iconRoot.Children.RemoveAll(child => iconNameSet.Contains(child.Name));
        foreach (string nodeName in L1IconNodeNames)
        {
            iconRoot.Children.Add(new BinaryXmlNode(nodeName));
        }

        iconBml.Data = BinaryXmlCodec.Write(iconRoot);
        SortFilesByNameWithoutExtension(targetIcon);
    }

    private static void PatchChallengeReady(
        RhoArchiveDocument target,
        RhoArchiveDocument donor)
    {
        for (int mission = 0x41; mission <= 0x46; mission++)
        {
            string folderName = mission.ToString("X2", CultureInfo.InvariantCulture);
            RhoArchiveFolder sourceFolder = RequireFolder(donor.Root, folderName);
            RhoArchiveFolder targetFolder = GetOrAddFolder(target.Root, folderName);
            RhoArchiveFile sourceDescription = sourceFolder.Files.SingleOrDefault(file =>
                    string.Equals(file.Name, "descript.bml", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException(
                    $"Donor stage_challengeReady/{folderName} is missing descript.bml.");
            BinaryXmlNode donorDescription = BinaryXmlCodec.Read(sourceDescription.Data);
            List<string> textureNames = BinaryXmlCodec.Enumerate(donorDescription)
                .Select(node => node.GetAttribute("texture"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            int expectedTextureCount = mission switch
            {
                0x41 or 0x42 or 0x44 => 3,
                0x43 or 0x45 or 0x46 => 4,
                _ => throw new InvalidOperationException("Unexpected L1 mission id."),
            };
            if (textureNames.Count != expectedTextureCount)
            {
                throw new InvalidDataException(
                    $"Donor mission {folderName} description references {textureNames.Count} media " +
                    $"textures; expected {expectedTextureCount}.");
            }

            foreach (string textureName in textureNames)
            {
                List<RhoArchiveFile> media = sourceFolder.Files.Where(file =>
                        string.Equals(
                            file.NameWithoutExtension,
                            textureName,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (media.Count != 1)
                {
                    throw new InvalidDataException(
                        $"Donor mission {folderName} must contain exactly one media file for '{textureName}'.");
                }

                EnsureExactFile(targetFolder, media[0]);
            }

            byte[] compatibleDescription = BuildP236Description(
                donorDescription,
                textureNames,
                mission);
            RhoArchiveFile? existingDescription = targetFolder.Files.FirstOrDefault(file =>
                string.Equals(file.Name, "descript.bml", StringComparison.OrdinalIgnoreCase));
            if (existingDescription is null)
            {
                targetFolder.AddFile(new RhoArchiveFile(
                    sourceDescription.Name,
                    compatibleDescription,
                    sourceDescription.Property));
            }
            else
            {
                existingDescription.Data = compatibleDescription;
            }

            SortFilesByNameWithoutExtension(targetFolder);
        }
    }

    private static byte[] BuildP236Description(
        BinaryXmlNode donorRoot,
        IReadOnlyList<string> textureNames,
        int mission)
    {
        string mapTexture = textureNames[0];
        string[] images = textureNames.Skip(1).ToArray();
        bool hasKoreanDonorText = BinaryXmlCodec.Enumerate(donorRoot).Any(node =>
            string.Equals(node.Name, "m", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(node.GetAttribute("c"), "kr", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(node.GetAttribute("v")));
        if (!hasKoreanDonorText)
        {
            throw new InvalidDataException(
                "Donor L1 description does not contain Korean StringBag text.");
        }

        string[] koreanText = GetCompatibleDescriptionText(mission);

        BinaryXmlNode root = Node("Container", ("windowRect", "0 0 696 483"));
        root.Children.Add(Node(
            "Panel",
            ("windowRect", "288 18 664 46"),
            ("uvRect", "0 0 376 28"),
            ("texture", "타이틀"),
            ("alphaTest", "true"),
            ("alphaBlend", "true")));
        root.Children.Add(Node(
            "Label",
            ("name", "challengeName"),
            ("windowRect", "321 26 470 46"),
            ("frame", "GlassButton"),
            ("textRender", "bold"),
            ("textColor", "white")));
        root.Children.Add(Node(
            "Label",
            ("name", "challengeTime"),
            ("windowRect", "499 26 660 46"),
            ("frame", "GlassButton"),
            ("textRender", "bold"),
            ("textColor", "white")));
        root.Children.Add(Node(
            "Panel",
            ("windowRect", "19 297 270 464"),
            ("uvRect", "0 0 251 167"),
            ("texture", mapTexture),
            ("alphaBlend", "true"),
            ("alphaTest", "true")));

        if (images.Length == 2)
        {
            AddDescriptionLabel(root, "288 79 664 150", "desc1");
            root.Children.Add(Node(
                "Panel",
                ("windowRect", "288 109 664 259"),
                ("texture", images[0])));
            AddDescriptionLabel(root, "288 287 664 150", "desc2");
            root.Children.Add(Node(
                "Panel",
                ("windowRect", "288 314 664 464"),
                ("texture", images[1])));
        }
        else if (images.Length == 3)
        {
            AddDescriptionLabel(root, "288 82 664 150", "desc1");
            root.Children.Add(Node(
                "Panel",
                ("windowRect", "288 109 664 319"),
                ("texture", images[0])));
            root.Children.Add(Node(
                "Panel",
                ("windowRect", "288 329 471 464"),
                ("texture", images[1])));
            root.Children.Add(Node(
                "Panel",
                ("windowRect", "481 329 664 464"),
                ("texture", images[2])));
            koreanText = [string.Join(" ", koreanText)];
        }
        else
        {
            throw new InvalidDataException("Unsupported L1 description image count.");
        }

        BinaryXmlNode stringBag = new("StringBag");
        int textCount = images.Length == 2 ? 2 : 1;
        for (int index = 0; index < textCount; index++)
        {
            string value = koreanText[Math.Min(index, koreanText.Length - 1)];
            BinaryXmlNode key = Node(
                "k",
                ("n", $"desc{index + 1}"));
            key.Children.Add(Node("m", ("c", "kr"), ("v", value)));
            stringBag.Children.Add(key);
        }

        root.Children.Add(stringBag);
        return BinaryXmlCodec.Write(root);
    }

    private static string[] GetCompatibleDescriptionText(int mission) => mission switch
    {
        0x41 =>
        [
            "주행 중에 발동하는 대마왕의 방해를 극복하면서 부스터 게이지를 모으세요.",
            "부스터를 적절히 사용하여 제한 시간 안에 코스를 완주하면 됩니다.",
        ],
        0x42 =>
        [
            "디지니와 아이템 대결을 합니다. 코스에서 물폭탄, 천사, 미사일을 획득하여 사용하세요.",
            "아이템으로 디지니를 견제하면서 제한 시간 안에 먼저 골인하면 됩니다.",
        ],
        0x43 =>
        [
            "좁은 고가 도로의 부스터존과 점프존을 이용하여 떨어지지 않고 제한 시간 안에 완주하세요.",
        ],
        0x44 =>
        [
            "다운힐에서 화살표를 따라 폴대 사이를 통과하며 내려오세요.",
            "연속 커브 지역을 지나 제한 시간 안에 코스를 완주하면 됩니다.",
        ],
        0x45 =>
        [
            "팩토리 미완성 5구역 트랙을 제한 시간 안에 완주하면 됩니다.",
        ],
        0x46 =>
        [
            "포레스트 아찔한 다운힐 트랙을 제한 시간 안에 완주하면 L1 코스를 모두 완료합니다.",
        ],
        _ => throw new InvalidDataException($"Unsupported L1 mission description: 0x{mission:X2}"),
    };

    private static void AddDescriptionLabel(
        BinaryXmlNode root,
        string windowRect,
        string key)
    {
        root.Children.Add(Node(
            "Label",
            ("windowRect", windowRect),
            ("frame", "GlassButton"),
            ("multiLine", "true"),
            ("autoWrap", "true"),
            ("lineGap", "3"),
            ("textRender", "bold"),
            ("text", $"#sb({key})")));
    }

    private static void AddForestFloorTexture(
        RhoArchiveDocument targetTheme,
        string donorData)
    {
        RhoArchiveDocument sourceTrack = RhoArchiveDocument.Open(
            Path.Combine(donorData, "track_forest_B01.rho"));
        List<RhoArchiveFile> matches = EnumerateFiles(sourceTrack.Root)
            .Select(pair => pair.File)
            .Where(file =>
                string.Equals(
                    file.Name,
                    "F_wood_d02.png",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                "Donor track_forest_B01.rho must contain exactly one F_wood_d02.png.");
        }

        RhoArchiveFolder texture = GetOrAddFolder(targetTheme.Root, "texture");
        EnsureExactFile(texture, matches[0]);
    }

    private static void MergeSelectedRootFiles(
        RhoArchiveFolder target,
        RhoArchiveFolder source,
        IEnumerable<string> fileNames)
    {
        foreach (string fileName in fileNames)
        {
            List<(string Path, RhoArchiveFile File)> matches = EnumerateFiles(source)
                .Where(pair =>
                    string.Equals(pair.Path, fileName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
            {
                throw new InvalidDataException(
                    $"Donor RHO must contain exactly one '{fileName}' entry.");
            }

            EnsureExactFile(target, matches[0].File);
        }
    }

    private static void ValidateThemeFingerprint(
        string archiveName,
        RhoArchiveFolder target,
        RhoArchiveFolder donor)
    {
        if (!ThemeFingerprints.TryGetValue(archiveName, out ThemeFingerprint? fingerprint))
        {
            throw new InvalidOperationException(
                $"No theme fingerprint exists for {archiveName}.");
        }

        ArchiveMergeDifference difference = CompareArchiveTrees(target, donor);
        if (difference.MissingPaths.Count is not 0 &&
            difference.MissingPaths.Count != fingerprint.CleanMissingCount)
        {
            throw new InvalidDataException(
                $"{archiveName} has {difference.MissingPaths.Count} donor additions remaining; " +
                $"expected either 0 or {fingerprint.CleanMissingCount}.");
        }

        if (!difference.ConflictPaths.SetEquals(fingerprint.TargetPreservedConflicts))
        {
            throw new InvalidDataException(
                $"{archiveName} does not match the supported P236/donor collision fingerprint.");
        }
    }

    private static void ValidateMissingOnlyFingerprint(
        string archiveName,
        RhoArchiveFolder target,
        RhoArchiveFolder donor,
        IEnumerable<string> cleanMissingPaths)
    {
        ArchiveMergeDifference difference = CompareArchiveTrees(target, donor);
        HashSet<string> expectedMissing = new(
            cleanMissingPaths,
            StringComparer.OrdinalIgnoreCase);
        if (difference.ConflictPaths.Count != 0 ||
            difference.MissingPaths.Count != 0 &&
            !difference.MissingPaths.SetEquals(expectedMissing))
        {
            throw new InvalidDataException(
                $"{archiveName} does not match the supported missing-only merge fingerprint.");
        }
    }

    private static ArchiveMergeDifference CompareArchiveTrees(
        RhoArchiveFolder target,
        RhoArchiveFolder donor)
    {
        Dictionary<string, RhoArchiveFile> targetFiles = EnumerateFiles(target)
            .ToDictionary(
                pair => pair.Path,
                pair => pair.File,
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> conflicts = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, RhoArchiveFile donorFile) in EnumerateFiles(donor))
        {
            if (!targetFiles.TryGetValue(path, out RhoArchiveFile? targetFile))
            {
                missing.Add(path);
            }
            else if (targetFile.Property != donorFile.Property ||
                     !targetFile.Data.AsSpan().SequenceEqual(donorFile.Data))
            {
                conflicts.Add(path);
            }
        }

        return new ArchiveMergeDifference(missing, conflicts);
    }

    private static void SortFilesByNameWithoutExtension(RhoArchiveFolder folder)
    {
        List<RhoArchiveFile> ordered = folder.Files
            .OrderBy(file => file.NameWithoutExtension, StringComparer.Ordinal)
            .ToList();
        folder.Files.Clear();
        folder.Files.AddRange(ordered);
    }

    private static void MergeMissingTree(
        RhoArchiveFolder target,
        RhoArchiveFolder source)
    {
        foreach (RhoArchiveFile sourceFile in source.Files)
        {
            if (!target.Files.Any(file =>
                    string.Equals(file.Name, sourceFile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                target.AddFile(CloneFile(sourceFile));
            }
        }

        foreach (RhoArchiveFolder sourceFolder in source.Folders)
        {
            RhoArchiveFolder targetFolder = GetOrAddFolder(target, sourceFolder.Name);
            MergeMissingTree(targetFolder, sourceFolder);
        }
    }

    private static void EnsureExactFile(
        RhoArchiveFolder target,
        RhoArchiveFile source)
    {
        RhoArchiveFile? existing = target.Files.FirstOrDefault(file =>
            string.Equals(file.Name, source.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            target.AddFile(CloneFile(source));
            return;
        }

        if (!existing.Data.AsSpan().SequenceEqual(source.Data))
        {
            throw new InvalidDataException(
                $"Target RHO already contains a different '{source.Name}' entry.");
        }
    }

    private static RhoArchiveFile CloneFile(RhoArchiveFile source) =>
        new(source.Name, source.Data.ToArray(), source.Property);

    private static RhoArchiveFolder GetOrAddFolder(
        RhoArchiveFolder parent,
        string name)
    {
        RhoArchiveFolder? folder = parent.Folders.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return folder ?? parent.AddFolder(name);
    }

    private static RhoArchiveFolder RequireFolder(
        RhoArchiveFolder parent,
        string name) =>
        parent.Folders.SingleOrDefault(folder =>
            string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"RHO is missing folder '{name}'.");

    private static RhoArchiveFile RequireArchiveFile(
        RhoArchiveFolder root,
        string path) =>
        root.GetFile(path)
        ?? throw new InvalidDataException($"RHO is missing '{path}'.");

    private static BinaryXmlNode ReadBml(
        RhoArchiveDocument archive,
        string path) =>
        BinaryXmlCodec.Read(RequireArchiveFile(archive.Root, path).Data);

    private static IEnumerable<(string Path, RhoArchiveFile File)> EnumerateFiles(
        RhoArchiveFolder root,
        string prefix = "")
    {
        foreach (RhoArchiveFile file in root.Files)
        {
            yield return (string.IsNullOrEmpty(prefix) ? file.Name : $"{prefix}/{file.Name}", file);
        }

        foreach (RhoArchiveFolder folder in root.Folders)
        {
            string childPrefix = string.IsNullOrEmpty(prefix)
                ? folder.Name
                : $"{prefix}/{folder.Name}";
            foreach ((string path, RhoArchiveFile file) in EnumerateFiles(folder, childPrefix))
            {
                yield return (path, file);
            }
        }
    }

    private static BinaryXmlNode RequireUniqueChildByAttribute(
        BinaryXmlNode root,
        string nodeName,
        string attributeName,
        string value)
    {
        List<BinaryXmlNode> matches = root.Children.Where(child =>
                string.Equals(child.Name, nodeName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    child.GetAttribute(attributeName),
                    value,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"BML must contain exactly one {nodeName} with {attributeName}='{value}'.");
        }

        return matches[0];
    }

    private static BinaryXmlNode CloneNode(BinaryXmlNode source)
    {
        BinaryXmlNode clone = new(source.Name)
        {
            Text = source.Text,
        };
        foreach (BinaryXmlAttribute attribute in source.Attributes)
        {
            clone.Attributes.Add(new BinaryXmlAttribute(attribute.Name, attribute.Value));
        }

        foreach (BinaryXmlNode child in source.Children)
        {
            clone.Children.Add(CloneNode(child));
        }

        return clone;
    }

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

    private static void RemoveAttributes(
        BinaryXmlNode node,
        params string[] names)
    {
        HashSet<string> remove = new(names, StringComparer.OrdinalIgnoreCase);
        node.Attributes.RemoveAll(attribute => remove.Contains(attribute.Name));
    }

    private static void MoveAttributeAfter(
        BinaryXmlNode node,
        string attributeName,
        string anchorName)
    {
        int attributeIndex = node.Attributes.FindIndex(attribute =>
            string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase));
        int anchorIndex = node.Attributes.FindIndex(attribute =>
            string.Equals(attribute.Name, anchorName, StringComparison.OrdinalIgnoreCase));
        if (attributeIndex < 0 || anchorIndex < 0)
        {
            throw new InvalidDataException(
                $"Cannot order challenge attribute '{attributeName}' after '{anchorName}'.");
        }

        BinaryXmlAttribute attribute = node.Attributes[attributeIndex];
        node.Attributes.RemoveAt(attributeIndex);
        anchorIndex = node.Attributes.FindIndex(candidate =>
            string.Equals(candidate.Name, anchorName, StringComparison.OrdinalIgnoreCase));
        node.Attributes.Insert(anchorIndex + 1, attribute);
    }

    private static void RequireFile(string directory, string name, string role)
    {
        string path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{role}에 필요한 파일이 없습니다: {name}",
                path);
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        FileInfo leftInfo = new(left);
        FileInfo rightInfo = new(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using FileStream leftStream = File.OpenRead(left);
        using FileStream rightStream = File.OpenRead(right);
        Span<byte> leftBuffer = stackalloc byte[16 * 1024];
        Span<byte> rightBuffer = stackalloc byte[16 * 1024];
        while (true)
        {
            int leftRead = leftStream.Read(leftBuffer);
            int rightRead = rightStream.Read(rightBuffer);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            if (!leftBuffer[..leftRead].SequenceEqual(rightBuffer[..rightRead]))
            {
                return false;
            }
        }
    }

    private static void TryDeleteBuildDirectory(string path)
    {
        try
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (name.StartsWith(".p236-l1-data-build-", StringComparison.Ordinal) &&
                Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Generated data is disposable. A later run can remove it manually.
        }
        catch (UnauthorizedAccessException)
        {
            // Generated data is disposable. A later run can remove it manually.
        }
    }

    private static void TryDeleteDonorSnapshotDirectory(string path)
    {
        try
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (name.StartsWith(".p236-l1-donor-snapshot-", StringComparison.Ordinal) &&
                Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // The per-invocation donor snapshot is disposable.
        }
        catch (UnauthorizedAccessException)
        {
            // The per-invocation donor snapshot is disposable.
        }
    }

    private sealed record ThemeFingerprint(
        int CleanMissingCount,
        IReadOnlyCollection<string> TargetPreservedConflicts);

    private sealed record ArchiveMergeDifference(
        HashSet<string> MissingPaths,
        HashSet<string> ConflictPaths);
}
