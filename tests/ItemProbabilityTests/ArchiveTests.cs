using System.Globalization;
using System.Text;
using System.Text.Json;
using KartRider.P236.ItemProbabilities.Internal;

namespace KartRider.P236.ItemProbabilities.Tests;

public sealed class ArchiveTests
{
    [Fact]
    public void Import_ReadsAllFiveTablesWithExactOrderNamesAndWeights()
    {
        using TestP236Data fixture = TestP236Data.Create();

        ItemProbabilityConfiguration configuration = P236ItemProbabilityArchive.Import(fixture.DataDirectory);

        Assert.Equal(new[] { 101, 102 }, configuration.Individual.Select(entry => entry.ItemId));
        Assert.Equal("바나나", configuration.Individual[0].Name);
        Assert.Equal((3, 5, 7), (
            configuration.Individual[0].HighRank,
            configuration.Individual[0].MidRank,
            configuration.Individual[0].LowRank));
        Assert.Equal(new[] { 201, 202 }, configuration.Team.Select(entry => entry.ItemId));
        Assert.Equal(0, configuration.Team[0].HighRank);
        Assert.Equal(new[] { 301, 302, 303 }, configuration.Flag.Select(entry => entry.ItemId));
        Assert.Equal(new[] { 23, 29 }, configuration.IndividualBonus.Select(entry => entry.Weight));
        Assert.Equal(new[] { 31, 37 }, configuration.TeamBonus.Select(entry => entry.Weight));
    }

    [Fact]
    public void Apply_ImportedConfigurationPreservesOmittedZeroWeightAttributesAsNoOp()
    {
        using TestP236Data fixture = TestP236Data.Create();
        byte[] originalItem = File.ReadAllBytes(fixture.ItemPath);
        byte[] originalMetadata = File.ReadAllBytes(fixture.MetadataPath);
        ItemProbabilityConfiguration imported =
            P236ItemProbabilityArchive.Import(fixture.DataDirectory);

        ItemProbabilityApplyResult result =
            P236ItemProbabilityArchive.Apply(fixture.DataDirectory, imported);

        Assert.False(result.Changed);
        Assert.Equal(originalItem, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(originalMetadata, File.ReadAllBytes(fixture.MetadataPath));
        Assert.False(File.Exists(result.ItemBackupPath));
        Assert.False(File.Exists(result.MetadataBackupPath));
    }

    [Fact]
    public void Apply_UpdatesAllTablesAndMetadataWhilePreservingUnrelatedContentAndBackups()
    {
        using TestP236Data fixture = TestP236Data.Create();
        byte[] originalItem = File.ReadAllBytes(fixture.ItemPath);
        byte[] originalMetadata = File.ReadAllBytes(fixture.MetadataPath);
        RhoArchiveDocument before = RhoArchiveDocument.Open(fixture.ItemPath);
        byte[] unrelated = before.Root.GetFile("unrelated.bin")!.Data.ToArray();
        ItemProbabilityConfiguration desired = TestP236Data.Clone(
            P236ItemProbabilityArchive.Import(fixture.DataDirectory));
        desired.Individual[0].HighRank = 101;
        desired.Team[1].MidRank = 202;
        desired.Flag[2].LowRank = 303;
        desired.IndividualBonus[0].Weight = 404;
        desired.TeamBonus[1].Weight = 505;

        ItemProbabilityApplyResult result = P236ItemProbabilityArchive.Apply(fixture.DataDirectory, desired);

        Assert.True(result.Changed);
        Assert.False(result.RecoveredInterruptedApply);
        Assert.Equal(originalItem, File.ReadAllBytes(result.ItemBackupPath));
        Assert.Equal(originalMetadata, File.ReadAllBytes(result.MetadataBackupPath));

        ItemProbabilityConfiguration actual = P236ItemProbabilityArchive.Import(fixture.DataDirectory);
        Assert.Equal(101, actual.Individual[0].HighRank);
        Assert.Equal(202, actual.Team[1].MidRank);
        Assert.Equal(303, actual.Flag[2].LowRank);
        Assert.Equal(404, actual.IndividualBonus[0].Weight);
        Assert.Equal(505, actual.TeamBonus[1].Weight);

        RhoArchiveDocument after = RhoArchiveDocument.Open(fixture.ItemPath);
        Assert.Equal(unrelated, after.Root.GetFile("unrelated.bin")!.Data);
        BinaryXmlNode individual = BinaryXmlCodec.Read(after.Root.GetFile("slot/itemProb@zz.bml")!.Data);
        Assert.Equal("keep-101", individual.Children[0].GetAttribute("unrelated"));

        RhoArchiveMetadata metadata = RhoArchiveDocument.ReadMetadata(fixture.ItemPath);
        AaaMetadataDocument aaa = AaaMetadataDocument.Open(fixture.MetadataPath);
        Assert.Equal(TestP236Data.MetadataEncoding, aaa.Encoding);
        aaa.ValidateItemArchive(metadata);
        Assert.Equal("root-value", aaa.Root.GetAttribute("keep"));
        BinaryXmlNode itemEntry = BinaryXmlCodec.Enumerate(aaa.Root).Single(node =>
            string.Equals(node.GetAttribute("fileName"), "item.rho", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("item-value", itemEntry.GetAttribute("keep"));
        Assert.Contains(aaa.Root.Children, node => node.GetAttribute("fileName") == "track.rho");
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        using TestP236Data fixture = TestP236Data.Create();
        ItemProbabilityConfiguration desired = TestP236Data.Clone(
            P236ItemProbabilityArchive.Import(fixture.DataDirectory));
        desired.Individual[0].LowRank++;
        Assert.True(P236ItemProbabilityArchive.Apply(fixture.DataDirectory, desired).Changed);
        byte[] itemAfterFirst = File.ReadAllBytes(fixture.ItemPath);
        byte[] metadataAfterFirst = File.ReadAllBytes(fixture.MetadataPath);
        byte[] backupAfterFirst = File.ReadAllBytes(
            Path.Combine(fixture.DataDirectory, P236ItemProbabilityTransaction.ItemBackupFileName));

        ItemProbabilityApplyResult second = P236ItemProbabilityArchive.Apply(fixture.DataDirectory, desired);

        Assert.False(second.Changed);
        Assert.Equal(itemAfterFirst, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(metadataAfterFirst, File.ReadAllBytes(fixture.MetadataPath));
        Assert.Equal(backupAfterFirst, File.ReadAllBytes(second.ItemBackupPath));
    }

    [Fact]
    public void Apply_RefusesToInvalidateAManagedL1Install()
    {
        using TestP236Data fixture = TestP236Data.Create();
        ItemProbabilityConfiguration desired = TestP236Data.Clone(
            P236ItemProbabilityArchive.Import(fixture.DataDirectory));
        desired.Individual[0].LowRank++;
        byte[] originalItem = File.ReadAllBytes(fixture.ItemPath);
        byte[] originalMetadata = File.ReadAllBytes(fixture.MetadataPath);
        File.WriteAllBytes(
            Path.Combine(fixture.DataDirectory, P236L1DataTransaction.InstallFileName),
            Encoding.UTF8.GetBytes("managed"));

        Assert.Throws<InvalidOperationException>(() =>
            P236ItemProbabilityArchive.Apply(fixture.DataDirectory, desired));
        Assert.Equal(originalItem, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(originalMetadata, File.ReadAllBytes(fixture.MetadataPath));
    }

    [Fact]
    public void Apply_RequiresExactIdsAndNamesBeforeWriting()
    {
        using TestP236Data fixture = TestP236Data.Create();
        byte[] item = File.ReadAllBytes(fixture.ItemPath);
        byte[] metadata = File.ReadAllBytes(fixture.MetadataPath);
        ItemProbabilityConfiguration wrongId = TestP236Data.Clone(
            P236ItemProbabilityArchive.Import(fixture.DataDirectory));
        wrongId.Flag[0].ItemId = 9999;

        Assert.Throws<ItemProbabilityConfigurationException>(() =>
            P236ItemProbabilityArchive.Apply(fixture.DataDirectory, wrongId));
        Assert.Equal(item, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(metadata, File.ReadAllBytes(fixture.MetadataPath));

        ItemProbabilityConfiguration wrongName = TestP236Data.Clone(
            P236ItemProbabilityArchive.Import(fixture.DataDirectory));
        wrongName.TeamBonus[0].Name = "different";
        Assert.Throws<ItemProbabilityConfigurationException>(() =>
            P236ItemProbabilityArchive.Apply(fixture.DataDirectory, wrongName));
        Assert.Equal(item, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(metadata, File.ReadAllBytes(fixture.MetadataPath));
    }

    [Fact]
    public void RecoverInterruptedApply_RestoresBothFilesFromPreparedTransaction()
    {
        using TestP236Data fixture = TestP236Data.Create();
        byte[] originalItem = File.ReadAllBytes(fixture.ItemPath);
        byte[] originalMetadata = File.ReadAllBytes(fixture.MetadataPath);
        Guid id = Guid.NewGuid();
        string transactionDirectory = Path.Combine(
            fixture.DataDirectory,
            $".p236-item-probabilities-txn-{id:N}");
        string originalDirectory = Path.Combine(transactionDirectory, "original");
        Directory.CreateDirectory(originalDirectory);
        File.Copy(fixture.ItemPath, Path.Combine(originalDirectory, "item.rho"));
        File.Copy(fixture.MetadataPath, Path.Combine(originalDirectory, "aaa.pk"));
        File.WriteAllBytes(fixture.ItemPath, Encoding.ASCII.GetBytes("partially-replaced-item"));
        File.WriteAllBytes(fixture.MetadataPath, Encoding.ASCII.GetBytes("old-metadata-still-live"));
        string markerPath = P236ItemProbabilityTransaction.GetMarkerPath(fixture.DataDirectory);
        File.WriteAllText(markerPath, JsonSerializer.Serialize(new
        {
            version = 1,
            transactionId = id.ToString("N"),
            phase = ItemProbabilityTransactionPhase.ItemReplaced.ToString(),
        }));

        bool recovered = P236ItemProbabilityArchive.RecoverInterruptedApply(fixture.DataDirectory);

        Assert.True(recovered);
        Assert.Equal(originalItem, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(originalMetadata, File.ReadAllBytes(fixture.MetadataPath));
        Assert.False(File.Exists(markerPath));
        Assert.False(Directory.Exists(transactionDirectory));
    }

    [Fact]
    public void Import_MalformedRecoveryMarkerIsPreservedAndSurfaced()
    {
        using TestP236Data fixture = TestP236Data.Create();
        string markerPath = P236ItemProbabilityTransaction.GetMarkerPath(fixture.DataDirectory);
        byte[] malformed = Encoding.UTF8.GetBytes("{ definitely-not-json");
        File.WriteAllBytes(markerPath, malformed);

        Assert.Throws<InvalidDataException>(() =>
            P236ItemProbabilityArchive.Import(fixture.DataDirectory));

        Assert.Equal(malformed, File.ReadAllBytes(markerPath));
    }

    [Fact]
    public void Apply_MismatchedSnapshotPairFailsBeforeWritingOrCreatingBackups()
    {
        using TestP236Data fixture = TestP236Data.Create();
        ItemProbabilityConfiguration desired = TestP236Data.Clone(
            P236ItemProbabilityArchive.Import(fixture.DataDirectory));
        desired.Individual[0].HighRank++;

        RhoArchiveMetadata metadata = RhoArchiveDocument.ReadMetadata(fixture.ItemPath);
        AaaMetadataDocument aaa = AaaMetadataDocument.Open(fixture.MetadataPath);
        aaa.UpdateItemArchive(metadata with { DataHash = metadata.DataHash ^ 0x01010101u });
        aaa.Save(fixture.MetadataPath);
        byte[] liveItem = File.ReadAllBytes(fixture.ItemPath);
        byte[] liveMetadata = File.ReadAllBytes(fixture.MetadataPath);

        Assert.Throws<InvalidDataException>(() =>
            P236ItemProbabilityArchive.Apply(fixture.DataDirectory, desired));

        Assert.Equal(liveItem, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(liveMetadata, File.ReadAllBytes(fixture.MetadataPath));
        Assert.False(File.Exists(Path.Combine(
            fixture.DataDirectory,
            P236ItemProbabilityTransaction.ItemBackupFileName)));
        Assert.False(File.Exists(Path.Combine(
            fixture.DataDirectory,
            P236ItemProbabilityTransaction.MetadataBackupFileName)));
        Assert.False(File.Exists(P236ItemProbabilityTransaction.GetMarkerPath(fixture.DataDirectory)));
        Assert.Empty(Directory.EnumerateDirectories(
            fixture.DataDirectory,
            ".p236-item-probabilities-txn-*"));
    }

    [Fact]
    public void Commit_ExternalLiveChangeIsDetectedAndPreservedWithoutRecovery()
    {
        using TestP236Data fixture = TestP236Data.Create();
        byte[] originalMetadata = File.ReadAllBytes(fixture.MetadataPath);
        byte[] externalItem = Encoding.UTF8.GetBytes("externally replaced item.rho");

        using P236ItemProbabilityTransaction transaction =
            P236ItemProbabilityTransaction.Acquire(fixture.DataDirectory);
        P236ItemProbabilityTransaction.TransactionFiles files =
            transaction.Prepare(fixture.ItemPath, fixture.MetadataPath);
        File.Copy(files.OriginalItemPath, files.ReplacementItemPath);
        File.Copy(files.OriginalMetadataPath, files.ReplacementMetadataPath);
        File.WriteAllBytes(fixture.ItemPath, externalItem);

        IOException exception = Assert.Throws<IOException>(() =>
            transaction.Commit(files, fixture.ItemPath, fixture.MetadataPath));

        Assert.Contains("changed outside this transaction", exception.Message);
        Assert.Equal(externalItem, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(originalMetadata, File.ReadAllBytes(fixture.MetadataPath));
        Assert.False(File.Exists(P236ItemProbabilityTransaction.GetMarkerPath(fixture.DataDirectory)));

        transaction.AbandonUnmarked(files);
        Assert.False(Directory.Exists(files.TransactionDirectory));
    }

    [Fact]
    public void Prepare_SecondSourceFailureImmediatelyCleansUnmarkedTransactionDirectory()
    {
        using TestP236Data fixture = TestP236Data.Create();
        string missingMetadata = Path.Combine(fixture.DataDirectory, "missing-aaa.pk");

        using P236ItemProbabilityTransaction transaction =
            P236ItemProbabilityTransaction.Acquire(fixture.DataDirectory);
        Assert.Throws<FileNotFoundException>(() =>
            transaction.Prepare(fixture.ItemPath, missingMetadata));

        Assert.Empty(Directory.EnumerateDirectories(
            fixture.DataDirectory,
            ".p236-item-probabilities-txn-*"));
        Assert.False(File.Exists(P236ItemProbabilityTransaction.GetMarkerPath(fixture.DataDirectory)));
    }

    [Fact]
    public void RecoverInterruptedApply_CommittedMarkerOnlyCleansAndNeverRollsBackLivePair()
    {
        using TestP236Data fixture = TestP236Data.Create();
        byte[] committedItem = Encoding.UTF8.GetBytes("committed item replacement");
        byte[] committedMetadata = Encoding.UTF8.GetBytes("committed metadata replacement");
        Guid id = Guid.NewGuid();
        string transactionDirectory = Path.Combine(
            fixture.DataDirectory,
            $".p236-item-probabilities-txn-{id:N}");
        string originalDirectory = Path.Combine(transactionDirectory, "original");
        Directory.CreateDirectory(originalDirectory);
        File.Copy(fixture.ItemPath, Path.Combine(originalDirectory, "item.rho"));
        File.Copy(fixture.MetadataPath, Path.Combine(originalDirectory, "aaa.pk"));
        File.WriteAllBytes(fixture.ItemPath, committedItem);
        File.WriteAllBytes(fixture.MetadataPath, committedMetadata);
        string markerPath = P236ItemProbabilityTransaction.GetMarkerPath(fixture.DataDirectory);
        File.WriteAllText(markerPath, JsonSerializer.Serialize(new
        {
            version = 1,
            transactionId = id.ToString("N"),
            phase = ItemProbabilityTransactionPhase.Committed.ToString(),
        }));

        Assert.True(P236ItemProbabilityArchive.RecoverInterruptedApply(fixture.DataDirectory));

        Assert.Equal(committedItem, File.ReadAllBytes(fixture.ItemPath));
        Assert.Equal(committedMetadata, File.ReadAllBytes(fixture.MetadataPath));
        Assert.False(File.Exists(markerPath));
        Assert.False(Directory.Exists(transactionDirectory));
    }
}
