using System.Text;
using System.Text.Json;

namespace KartRider.P236.ItemProbabilities.Tests;

public sealed class ConfigurationStoreTests
{
    [Fact]
    public void SaveAndLoad_UsesOrderedCamelCaseJsonV1()
    {
        using TestP236Data fixture = TestP236Data.Create();
        ItemProbabilityConfiguration imported = P236ItemProbabilityArchive.Import(fixture.DataDirectory);
        string path = Path.Combine(fixture.RootDirectory, "probabilities.json");

        ItemProbabilityConfigurationStore.Save(path, imported);
        string json = File.ReadAllText(path);
        ItemProbabilityConfiguration loaded = ItemProbabilityConfigurationStore.Load(path);

        Assert.Contains("\"individualBonus\"", json, StringComparison.Ordinal);
        Assert.Contains("\"itemId\"", json, StringComparison.Ordinal);
        Assert.Contains("바나나", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ItemId\"", json, StringComparison.Ordinal);
        Assert.Equal(imported.Individual.Select(entry => entry.ItemId), loaded.Individual.Select(entry => entry.ItemId));
        Assert.Equal(imported.TeamBonus.Select(entry => entry.ItemId), loaded.TeamBonus.Select(entry => entry.ItemId));

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void Load_MalformedJsonIsPreservedAndSurfaced()
    {
        using TestP236Data fixture = TestP236Data.Create();
        string path = Path.Combine(fixture.RootDirectory, "bad.json");
        byte[] malformed = Encoding.UTF8.GetBytes("{ not-json: [");
        File.WriteAllBytes(path, malformed);

        Assert.Throws<ItemProbabilityConfigurationException>(() =>
            ItemProbabilityConfigurationStore.Load(path));

        Assert.Equal(malformed, File.ReadAllBytes(path));
    }

    [Fact]
    public void Validate_RejectsInvalidTablesAndWeights()
    {
        using TestP236Data fixture = TestP236Data.Create();
        ItemProbabilityConfiguration valid = P236ItemProbabilityArchive.Import(fixture.DataDirectory);

        ItemProbabilityConfiguration duplicate = TestP236Data.Clone(valid);
        duplicate.Individual.Add(TestP236Data.Clone(valid).Individual[0]);
        Assert.Throws<ItemProbabilityConfigurationException>(duplicate.Validate);

        ItemProbabilityConfiguration zeroColumn = TestP236Data.Clone(valid);
        zeroColumn.Flag.ForEach(entry => entry.MidRank = 0);
        Assert.Throws<ItemProbabilityConfigurationException>(zeroColumn.Validate);

        ItemProbabilityConfiguration outOfRange = TestP236Data.Clone(valid);
        outOfRange.Team[0].HighRank = ItemProbabilityEntry.MaximumWeight + 1;
        Assert.Throws<ItemProbabilityConfigurationException>(outOfRange.Validate);

        ItemProbabilityConfiguration blankName = TestP236Data.Clone(valid);
        blankName.IndividualBonus[0].Name = " ";
        Assert.Throws<ItemProbabilityConfigurationException>(blankName.Validate);

        ItemProbabilityConfiguration zeroBonus = TestP236Data.Clone(valid);
        zeroBonus.TeamBonus.ForEach(entry => entry.Weight = 0);
        Assert.Throws<ItemProbabilityConfigurationException>(zeroBonus.Validate);
    }
}
