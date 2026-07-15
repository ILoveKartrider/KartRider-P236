using System.Globalization;

namespace KartRider.P236.ItemProbabilities.Internal;

internal sealed class ItemProbabilityArchiveContext
{
    internal const string IndividualPath = "slot/itemProb@zz.bml";
    internal const string TeamPath = "slot/itemProb_team@zz.bml";
    internal const string FlagPath = "slot/itemProb_flag@zz.bml";
    internal const string IndividualBonusPath = "slot/itemProb_bonus@zz.bml";
    internal const string TeamBonusPath = "slot/itemProb_team_bonus@zz.bml";

    private readonly Dictionary<string, ProbabilityTable> _tables;

    private ItemProbabilityArchiveContext(
        RhoArchiveDocument archive,
        Dictionary<string, ProbabilityTable> tables,
        ItemProbabilityConfiguration configuration)
    {
        Archive = archive;
        _tables = tables;
        Configuration = configuration;
    }

    internal RhoArchiveDocument Archive { get; }

    internal ItemProbabilityConfiguration Configuration { get; }

    internal static ItemProbabilityArchiveContext Open(string itemArchivePath)
    {
        RhoArchiveDocument archive = RhoArchiveDocument.Open(itemArchivePath);
        Dictionary<string, ProbabilityTable> tables = new(StringComparer.OrdinalIgnoreCase);
        ProbabilityTable individual = ReadTable(archive, IndividualPath, bonus: false);
        ProbabilityTable team = ReadTable(archive, TeamPath, bonus: false);
        ProbabilityTable flag = ReadTable(archive, FlagPath, bonus: false);
        ProbabilityTable individualBonus = ReadTable(archive, IndividualBonusPath, bonus: true);
        ProbabilityTable teamBonus = ReadTable(archive, TeamBonusPath, bonus: true);
        tables.Add(IndividualPath, individual);
        tables.Add(TeamPath, team);
        tables.Add(FlagPath, flag);
        tables.Add(IndividualBonusPath, individualBonus);
        tables.Add(TeamBonusPath, teamBonus);

        ItemProbabilityConfiguration configuration = new()
        {
            Version = ItemProbabilityConfiguration.CurrentVersion,
            Individual = individual.ToRankEntries(),
            Team = team.ToRankEntries(),
            Flag = flag.ToRankEntries(),
            IndividualBonus = individualBonus.ToBonusEntries(),
            TeamBonus = teamBonus.ToBonusEntries(),
        };
        configuration.Validate();
        return new ItemProbabilityArchiveContext(archive, tables, configuration);
    }

    internal bool Apply(ItemProbabilityConfiguration configuration)
    {
        configuration.Validate();
        bool changed = false;
        changed |= _tables[IndividualPath].ApplyRank(nameof(configuration.Individual), configuration.Individual);
        changed |= _tables[TeamPath].ApplyRank(nameof(configuration.Team), configuration.Team);
        changed |= _tables[FlagPath].ApplyRank(nameof(configuration.Flag), configuration.Flag);
        changed |= _tables[IndividualBonusPath].ApplyBonus(
            nameof(configuration.IndividualBonus),
            configuration.IndividualBonus);
        changed |= _tables[TeamBonusPath].ApplyBonus(nameof(configuration.TeamBonus), configuration.TeamBonus);
        return changed;
    }

    private static ProbabilityTable ReadTable(RhoArchiveDocument archive, string path, bool bonus)
    {
        RhoArchiveFile file = archive.Root.GetFile(path)
            ?? throw new InvalidDataException($"item.rho is missing required probability BML '{path}'.");
        BinaryXmlNode root = BinaryXmlCodec.Read(file.Data);
        if (!string.Equals(root.Name, "items", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{path} has an unexpected BML root.");
        }

        List<ProbabilityRow> rows = new();
        HashSet<int> itemIds = new();
        foreach (BinaryXmlNode node in root.Children)
        {
            if (!string.Equals(node.Name, "item", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{path} contains an unexpected '{node.Name}' row.");
            }

            int itemId = ParseIntAttribute(node, "idx", path);
            string name = node.GetAttribute("name")
                ?? throw new InvalidDataException($"{path} item {itemId} has no name attribute.");
            if (!itemIds.Add(itemId))
            {
                throw new InvalidDataException($"{path} contains duplicate item id {itemId}.");
            }

            rows.Add(bonus
                ? new ProbabilityRow(node, itemId, name, ParseWeightAttribute(node, "prob", path), 0, 0)
                : new ProbabilityRow(
                    node,
                    itemId,
                    name,
                    ParseWeightAttribute(node, "highrank", path),
                    ParseWeightAttribute(node, "midrank", path),
                    ParseWeightAttribute(node, "lowrank", path)));
        }

        if (rows.Count == 0)
        {
            throw new InvalidDataException($"{path} contains no item rows.");
        }

        return new ProbabilityTable(path, file, root, bonus, rows);
    }

    private static int ParseIntAttribute(BinaryXmlNode node, string name, string path)
    {
        string? value = node.GetAttribute(name);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            throw new InvalidDataException($"{path} contains an invalid {name} attribute.");
        }

        return result;
    }

    private static int ParseWeightAttribute(BinaryXmlNode node, string name, string path)
    {
        string? value = node.GetAttribute(name);
        if (value is null)
        {
            // Original P236 probability BML omits zero-valued rank fields.
            return 0;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            throw new InvalidDataException($"{path} contains an invalid {name} attribute.");
        }

        return result;
    }

    private sealed class ProbabilityTable
    {
        private readonly string _path;
        private readonly RhoArchiveFile _file;
        private readonly BinaryXmlNode _root;
        private readonly bool _bonus;
        private readonly List<ProbabilityRow> _rows;

        internal ProbabilityTable(
            string path,
            RhoArchiveFile file,
            BinaryXmlNode root,
            bool bonus,
            List<ProbabilityRow> rows)
        {
            _path = path;
            _file = file;
            _root = root;
            _bonus = bonus;
            _rows = rows;
        }

        internal List<ItemProbabilityEntry> ToRankEntries()
        {
            if (_bonus)
            {
                throw new InvalidOperationException("Bonus table cannot be converted to rank entries.");
            }

            return _rows.Select(row => new ItemProbabilityEntry
            {
                ItemId = row.ItemId,
                Name = row.Name,
                HighRank = row.First,
                MidRank = row.Second,
                LowRank = row.Third,
            }).ToList();
        }

        internal List<ItemProbabilityBonusEntry> ToBonusEntries()
        {
            if (!_bonus)
            {
                throw new InvalidOperationException("Rank table cannot be converted to bonus entries.");
            }

            return _rows.Select(row => new ItemProbabilityBonusEntry
            {
                ItemId = row.ItemId,
                Name = row.Name,
                Weight = row.First,
            }).ToList();
        }

        internal bool ApplyRank(string tableName, IReadOnlyCollection<ItemProbabilityEntry> entries)
        {
            if (_bonus)
            {
                throw new InvalidOperationException("Cannot apply rank weights to a bonus table.");
            }

            Dictionary<int, ItemProbabilityEntry> byId = entries.ToDictionary(entry => entry.ItemId);
            ValidateIdentitySet(tableName, byId.Count, byId.Keys, id => byId[id].Name);
            bool changed = false;
            foreach (ProbabilityRow row in _rows)
            {
                ItemProbabilityEntry entry = byId[row.ItemId];
                changed |= Set(row.Node, "highrank", entry.HighRank);
                changed |= Set(row.Node, "midrank", entry.MidRank);
                changed |= Set(row.Node, "lowrank", entry.LowRank);
            }

            if (changed)
            {
                _file.Data = BinaryXmlCodec.Write(_root);
            }

            return changed;
        }

        internal bool ApplyBonus(string tableName, IReadOnlyCollection<ItemProbabilityBonusEntry> entries)
        {
            if (!_bonus)
            {
                throw new InvalidOperationException("Cannot apply bonus weights to a rank table.");
            }

            Dictionary<int, ItemProbabilityBonusEntry> byId = entries.ToDictionary(entry => entry.ItemId);
            ValidateIdentitySet(tableName, byId.Count, byId.Keys, id => byId[id].Name);
            bool changed = false;
            foreach (ProbabilityRow row in _rows)
            {
                changed |= Set(row.Node, "prob", byId[row.ItemId].Weight);
            }

            if (changed)
            {
                _file.Data = BinaryXmlCodec.Write(_root);
            }

            return changed;
        }

        private void ValidateIdentitySet(
            string tableName,
            int configuredCount,
            IEnumerable<int> configuredIds,
            Func<int, string> configuredName)
        {
            HashSet<int> archiveIds = _rows.Select(row => row.ItemId).ToHashSet();
            if (configuredCount != archiveIds.Count || configuredIds.Any(id => !archiveIds.Contains(id)))
            {
                throw new ItemProbabilityConfigurationException(
                    $"{tableName} item IDs do not exactly match {_path}.");
            }

            foreach (ProbabilityRow row in _rows)
            {
                if (!string.Equals(row.Name, configuredName(row.ItemId), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ItemProbabilityConfigurationException(
                        $"{tableName} itemId {row.ItemId} name does not match {_path}.");
                }
            }
        }

        private static bool Set(BinaryXmlNode node, string name, int value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            string? current = node.GetAttribute(name);
            if (current == text || (current is null && value == 0))
            {
                return false;
            }

            node.SetAttribute(name, text);
            return true;
        }
    }

    private sealed record ProbabilityRow(
        BinaryXmlNode Node,
        int ItemId,
        string Name,
        int First,
        int Second,
        int Third);
}
