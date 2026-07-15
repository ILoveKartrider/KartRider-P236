using System.Globalization;

namespace KartRider.P236.ItemProbabilities;

/// <summary>A portable, ordered snapshot of the five live P236 item probability tables.</summary>
public sealed class ItemProbabilityConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<ItemProbabilityEntry> Individual { get; set; } = new();

    public List<ItemProbabilityEntry> Team { get; set; } = new();

    public List<ItemProbabilityEntry> Flag { get; set; } = new();

    public List<ItemProbabilityBonusEntry> IndividualBonus { get; set; } = new();

    public List<ItemProbabilityBonusEntry> TeamBonus { get; set; } = new();

    /// <summary>Validates the version, rows and all rank weight columns.</summary>
    public void Validate()
    {
        if (Version != CurrentVersion)
        {
            throw new ItemProbabilityConfigurationException(
                $"Unsupported item probability configuration version {Version.ToString(CultureInfo.InvariantCulture)}; " +
                $"expected {CurrentVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        ValidateTable(nameof(Individual), Individual);
        ValidateTable(nameof(Team), Team);
        ValidateTable(nameof(Flag), Flag);
        ValidateBonusTable(nameof(IndividualBonus), IndividualBonus);
        ValidateBonusTable(nameof(TeamBonus), TeamBonus);
    }

    private static void ValidateBonusTable(string tableName, List<ItemProbabilityBonusEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            throw new ItemProbabilityConfigurationException($"{tableName} must contain at least one item row.");
        }

        HashSet<int> itemIds = new();
        long sum = 0;
        try
        {
            foreach (ItemProbabilityBonusEntry? entry in entries)
            {
                if (entry is null)
                {
                    throw new ItemProbabilityConfigurationException($"{tableName} contains a null item row.");
                }

                if (!itemIds.Add(entry.ItemId))
                {
                    throw new ItemProbabilityConfigurationException(
                        $"{tableName} contains duplicate itemId {entry.ItemId.ToString(CultureInfo.InvariantCulture)}.");
                }

                ValidateName(tableName, entry.ItemId, entry.Name);
                ValidateWeight(tableName, entry.ItemId, "weight", entry.Weight);
                sum = checked(sum + entry.Weight);
            }
        }
        catch (OverflowException exception)
        {
            throw new ItemProbabilityConfigurationException(
                $"{tableName} has a weight sum too large to represent safely.", exception);
        }

        if (sum <= 0)
        {
            throw new ItemProbabilityConfigurationException($"{tableName} must have a positive total weight.");
        }
    }

    private static void ValidateTable(string tableName, List<ItemProbabilityEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            throw new ItemProbabilityConfigurationException($"{tableName} must contain at least one item row.");
        }

        HashSet<int> itemIds = new();
        long highRankSum = 0;
        long midRankSum = 0;
        long lowRankSum = 0;

        try
        {
            foreach (ItemProbabilityEntry? entry in entries)
            {
                if (entry is null)
                {
                    throw new ItemProbabilityConfigurationException($"{tableName} contains a null item row.");
                }

                if (!itemIds.Add(entry.ItemId))
                {
                    throw new ItemProbabilityConfigurationException(
                        $"{tableName} contains duplicate itemId {entry.ItemId.ToString(CultureInfo.InvariantCulture)}.");
                }

                ValidateName(tableName, entry);
                ValidateWeight(tableName, entry.ItemId, "highRank", entry.HighRank);
                ValidateWeight(tableName, entry.ItemId, "midRank", entry.MidRank);
                ValidateWeight(tableName, entry.ItemId, "lowRank", entry.LowRank);

                highRankSum = checked(highRankSum + entry.HighRank);
                midRankSum = checked(midRankSum + entry.MidRank);
                lowRankSum = checked(lowRankSum + entry.LowRank);
            }
        }
        catch (OverflowException exception)
        {
            throw new ItemProbabilityConfigurationException(
                $"{tableName} has a rank weight sum too large to represent safely.", exception);
        }

        if (highRankSum <= 0 || midRankSum <= 0 || lowRankSum <= 0)
        {
            throw new ItemProbabilityConfigurationException(
                $"Every {tableName} rank column must have a positive total weight.");
        }
    }

    private static void ValidateName(string tableName, ItemProbabilityEntry entry)
    {
        ValidateName(tableName, entry.ItemId, entry.Name);
    }

    private static void ValidateName(string tableName, int itemId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > ItemProbabilityEntry.MaximumNameLength ||
            name.Any(char.IsControl))
        {
            throw new ItemProbabilityConfigurationException(
                $"{tableName} itemId {itemId.ToString(CultureInfo.InvariantCulture)} has an invalid name.");
        }
    }

    private static void ValidateWeight(string tableName, int itemId, string column, int value)
    {
        if (value is < ItemProbabilityEntry.MinimumWeight or > ItemProbabilityEntry.MaximumWeight)
        {
            throw new ItemProbabilityConfigurationException(
                $"{tableName} itemId {itemId.ToString(CultureInfo.InvariantCulture)} {column} must be in " +
                $"{ItemProbabilityEntry.MinimumWeight.ToString(CultureInfo.InvariantCulture)}.." +
                $"{ItemProbabilityEntry.MaximumWeight.ToString(CultureInfo.InvariantCulture)}.");
        }
    }
}

/// <summary>An item row and its high-, middle- and low-rank integer weights.</summary>
public sealed class ItemProbabilityEntry
{
    public const int MinimumWeight = 0;
    public const int MaximumWeight = 1_000_000;
    public const int MaximumNameLength = 256;

    public int ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int HighRank { get; set; }

    public int MidRank { get; set; }

    public int LowRank { get; set; }
}

/// <summary>An item row from one of the P236 single-weight bonus banks.</summary>
public sealed class ItemProbabilityBonusEntry
{
    public int ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Weight { get; set; }
}

/// <summary>Reports invalid JSON model versions, rows, names or weights.</summary>
public sealed class ItemProbabilityConfigurationException : Exception
{
    public ItemProbabilityConfigurationException(string message)
        : base(message)
    {
    }

    public ItemProbabilityConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
