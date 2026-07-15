using KartRider.P236.ItemProbabilities.Internal;

namespace KartRider.P236.ItemProbabilities;

/// <summary>Imports or transactionally applies all five live P236 item probability banks.</summary>
public static class P236ItemProbabilityArchive
{
    public static ItemProbabilityConfiguration Import(string dataDirectory)
    {
        P236ItemProbabilityPaths paths = ResolvePaths(dataDirectory);
        string markerPath = P236ItemProbabilityTransaction.GetMarkerPath(paths.DataDirectory);
        if (File.Exists(markerPath))
        {
            using P236ItemProbabilityTransaction transaction =
                P236ItemProbabilityTransaction.Acquire(paths.DataDirectory);
            transaction.RecoverIfNeeded();
        }

        return ItemProbabilityArchiveContext.Open(paths.ItemArchivePath).Configuration;
    }

    public static ItemProbabilityApplyResult Apply(
        string dataDirectory,
        ItemProbabilityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        P236ItemProbabilityPaths paths = ResolvePaths(dataDirectory);

        using P236ItemProbabilityTransaction transaction =
            P236ItemProbabilityTransaction.Acquire(paths.DataDirectory);
        bool recovered = transaction.RecoverIfNeeded();
        P236ItemProbabilityTransaction.TransactionFiles files =
            transaction.Prepare(paths.ItemArchivePath, paths.MetadataPath);

        try
        {
            // Everything below is derived from the durable transaction snapshot,
            // never from live files that another process could replace mid-apply.
            ItemProbabilityArchiveContext context =
                ItemProbabilityArchiveContext.Open(files.OriginalItemPath);
            AaaMetadataDocument aaa = AaaMetadataDocument.Open(files.OriginalMetadataPath);

            // A syntactically valid but mismatched pair is not a safe baseline.
            // Validate before changing in-memory probability data or creating the
            // one-time backups.
            aaa.ValidateItemArchive(RhoArchiveDocument.ReadMetadata(files.OriginalItemPath));

            bool changed = context.Apply(configuration);
            if (!changed)
            {
                transaction.AbandonUnmarked(files);
                return CreateResult(paths, changed: false, recovered);
            }

            RhoArchiveMetadata metadata = context.Archive.Save(files.ReplacementItemPath);
            aaa.UpdateItemArchive(metadata);
            aaa.Save(files.ReplacementMetadataPath);

            // Full semantic read-back validation happens before either live
            // client file is replaced. Physical RHO block layout may change,
            // but the complete decoded tree, file properties and aaa.pk XML
            // plus its KRData mode/key must match the in-memory documents.
            ItemProbabilityArchiveContext generatedContext =
                ItemProbabilityArchiveContext.Open(files.ReplacementItemPath);
            ValidateAppliedValues(configuration, generatedContext.Configuration);
            context.Archive.ValidateSemanticallyEqual(generatedContext.Archive);
            AaaMetadataDocument generatedAaa =
                AaaMetadataDocument.Open(files.ReplacementMetadataPath);
            aaa.ValidateSemanticallyEqual(generatedAaa);
            generatedAaa.ValidateItemArchive(metadata);

            P236ItemProbabilityTransaction.EnsurePermanentBackup(
                files.OriginalItemPath,
                paths.ItemBackupPath);
            P236ItemProbabilityTransaction.EnsurePermanentBackup(
                files.OriginalMetadataPath,
                paths.MetadataBackupPath);
            transaction.Commit(files, paths.ItemArchivePath, paths.MetadataPath);
        }
        catch
        {
            if (File.Exists(P236ItemProbabilityTransaction.GetMarkerPath(paths.DataDirectory)))
            {
                transaction.RecoverIfNeeded();
            }
            else
            {
                transaction.AbandonUnmarked(files);
            }

            throw;
        }

        return CreateResult(paths, changed: true, recovered);
    }

    /// <summary>Rolls back an interrupted apply, or finalizes cleanup after a committed one.</summary>
    public static bool RecoverInterruptedApply(string dataDirectory)
    {
        P236ItemProbabilityPaths paths = ResolvePaths(dataDirectory);
        using P236ItemProbabilityTransaction transaction =
            P236ItemProbabilityTransaction.Acquire(paths.DataDirectory);
        return transaction.RecoverIfNeeded();
    }

    private static P236ItemProbabilityPaths ResolvePaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string fullDirectory = Path.GetFullPath(dataDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"P236 Data directory was not found: {fullDirectory}");
        }

        string itemPath = Path.Combine(fullDirectory, "item.rho");
        string metadataPath = Path.Combine(fullDirectory, "aaa.pk");
        if (!File.Exists(itemPath))
        {
            throw new FileNotFoundException("P236 item.rho was not found.", itemPath);
        }

        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("P236 aaa.pk was not found next to item.rho.", metadataPath);
        }

        return new P236ItemProbabilityPaths(
            fullDirectory,
            itemPath,
            metadataPath,
            Path.Combine(fullDirectory, P236ItemProbabilityTransaction.ItemBackupFileName),
            Path.Combine(fullDirectory, P236ItemProbabilityTransaction.MetadataBackupFileName));
    }

    private static ItemProbabilityApplyResult CreateResult(
        P236ItemProbabilityPaths paths,
        bool changed,
        bool recovered) =>
        new()
        {
            Changed = changed,
            RecoveredInterruptedApply = recovered,
            ItemArchivePath = paths.ItemArchivePath,
            MetadataPath = paths.MetadataPath,
            ItemBackupPath = paths.ItemBackupPath,
            MetadataBackupPath = paths.MetadataBackupPath,
        };

    private static void ValidateAppliedValues(
        ItemProbabilityConfiguration expected,
        ItemProbabilityConfiguration actual)
    {
        ValidateRank(expected.Individual, actual.Individual, nameof(expected.Individual));
        ValidateRank(expected.Team, actual.Team, nameof(expected.Team));
        ValidateRank(expected.Flag, actual.Flag, nameof(expected.Flag));
        ValidateBonus(expected.IndividualBonus, actual.IndividualBonus, nameof(expected.IndividualBonus));
        ValidateBonus(expected.TeamBonus, actual.TeamBonus, nameof(expected.TeamBonus));
    }

    private static void ValidateRank(
        IEnumerable<ItemProbabilityEntry> expected,
        IEnumerable<ItemProbabilityEntry> actual,
        string name)
    {
        Dictionary<int, ItemProbabilityEntry> expectedById = expected.ToDictionary(entry => entry.ItemId);
        Dictionary<int, ItemProbabilityEntry> actualById = actual.ToDictionary(entry => entry.ItemId);
        if (expectedById.Count != actualById.Count || expectedById.Any(pair =>
                !actualById.TryGetValue(pair.Key, out ItemProbabilityEntry? value) ||
                !string.Equals(pair.Value.Name, value.Name, StringComparison.OrdinalIgnoreCase) ||
                pair.Value.HighRank != value.HighRank ||
                pair.Value.MidRank != value.MidRank ||
                pair.Value.LowRank != value.LowRank))
        {
            throw new InvalidDataException($"Generated {name} probability table failed read-back validation.");
        }
    }

    private static void ValidateBonus(
        IEnumerable<ItemProbabilityBonusEntry> expected,
        IEnumerable<ItemProbabilityBonusEntry> actual,
        string name)
    {
        Dictionary<int, ItemProbabilityBonusEntry> expectedById = expected.ToDictionary(entry => entry.ItemId);
        Dictionary<int, ItemProbabilityBonusEntry> actualById = actual.ToDictionary(entry => entry.ItemId);
        if (expectedById.Count != actualById.Count || expectedById.Any(pair =>
                !actualById.TryGetValue(pair.Key, out ItemProbabilityBonusEntry? value) ||
                !string.Equals(pair.Value.Name, value.Name, StringComparison.OrdinalIgnoreCase) ||
                pair.Value.Weight != value.Weight))
        {
            throw new InvalidDataException($"Generated {name} probability table failed read-back validation.");
        }
    }

    private sealed record P236ItemProbabilityPaths(
        string DataDirectory,
        string ItemArchivePath,
        string MetadataPath,
        string ItemBackupPath,
        string MetadataBackupPath);
}

/// <summary>Describes what an apply changed and where its one-time backups live.</summary>
public sealed class ItemProbabilityApplyResult
{
    public bool Changed { get; set; }

    public bool RecoveredInterruptedApply { get; set; }

    public string ItemArchivePath { get; set; } = string.Empty;

    public string MetadataPath { get; set; } = string.Empty;

    public string ItemBackupPath { get; set; } = string.Empty;

    public string MetadataBackupPath { get; set; } = string.Empty;
}
