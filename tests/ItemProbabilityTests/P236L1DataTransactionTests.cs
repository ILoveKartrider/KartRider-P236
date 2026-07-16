using System.Text;
using System.Text.Json;
using KartRider.P236.ItemProbabilities.Internal;

namespace KartRider.P236.ItemProbabilities.Tests;

public sealed class P236L1DataTransactionTests
{
    [Fact]
    public void CommitIsIdempotentAndRestoreReturnsTheExactOriginalSet()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "KartRider.P236.L1DataTransactionTests",
            Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "Data");
        string generated = Path.Combine(root, "generated");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(generated);
        try
        {
            string liveTrack = Write(data, "track_common.rho", "original-track");
            string liveMetadata = Write(data, "aaa.pk", "original-metadata");
            string originalTrackHash = Hash(liveTrack);
            string originalMetadataHash = Hash(liveMetadata);

            Dictionary<string, string> replacements = new(StringComparer.OrdinalIgnoreCase)
            {
                ["track_common.rho"] = Write(generated, "track_common.rho", "patched-track"),
                ["track_factory_R01.rho"] = Write(generated, "track_factory_R01.rho", "new-track"),
                ["aaa.pk"] = Write(generated, "aaa.pk", "patched-metadata"),
            };

            using (P236L1DataTransaction transaction = P236L1DataTransaction.Acquire(data))
            {
                Assert.False(transaction.RecoverIfNeeded());
                P236L1DataCommitResult first = transaction.Commit(
                    replacements,
                    recipeVersion: 1,
                    progress: null);
                Assert.Equal(4, first.ChangedFileCount);
                Assert.True(File.Exists(Path.Combine(data, P236L1DataTransaction.InstallFileName)));
                Assert.Equal("patched-track", Read(liveTrack));
                Assert.Equal("new-track", Read(Path.Combine(data, "track_factory_R01.rho")));

                DateTime installWriteTime = File.GetLastWriteTimeUtc(
                    Path.Combine(data, P236L1DataTransaction.InstallFileName));
                P236L1DataCommitResult second = transaction.Commit(
                    replacements,
                    recipeVersion: 1,
                    progress: null);
                Assert.Equal(0, second.ChangedFileCount);
                Assert.Equal(
                    installWriteTime,
                    File.GetLastWriteTimeUtc(
                        Path.Combine(data, P236L1DataTransaction.InstallFileName)));

                P236L1DataCommitResult restored = transaction.Restore(progress: null);
                Assert.Equal(4, restored.ChangedFileCount);

                P236L1DataCommitResult reapplied = transaction.Commit(
                    replacements,
                    recipeVersion: 1,
                    progress: null);
                Assert.Equal(4, reapplied.ChangedFileCount);
                P236L1DataCommitResult restoredAgain = transaction.Restore(progress: null);
                Assert.Equal(4, restoredAgain.ChangedFileCount);
            }

            Assert.Equal(originalTrackHash, Hash(liveTrack));
            Assert.Equal(originalMetadataHash, Hash(liveMetadata));
            Assert.False(File.Exists(Path.Combine(data, "track_factory_R01.rho")));
            Assert.False(File.Exists(Path.Combine(data, P236L1DataTransaction.InstallFileName)));
            Assert.False(File.Exists(Path.Combine(data, P236L1DataTransaction.MarkerFileName)));
            Assert.True(Directory.Exists(
                Path.Combine(data, P236L1DataTransaction.BackupDirectoryName)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CommitRefusesAnExternallyChangedManagedFile()
    {
        string root = CreateRoot();
        string data = Path.Combine(root, "Data");
        string generated = Path.Combine(root, "generated");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(generated);
        try
        {
            Write(data, "track_common.rho", "original-track");
            Write(data, "aaa.pk", "original-metadata");
            Dictionary<string, string> replacements = CreateReplacements(generated);

            using P236L1DataTransaction transaction = P236L1DataTransaction.Acquire(data);
            transaction.Commit(replacements, recipeVersion: 1, progress: null);
            Write(data, "track_common.rho", "external-change");

            Assert.Throws<InvalidDataException>(() =>
                transaction.Commit(replacements, recipeVersion: 1, progress: null));
            Assert.Equal("external-change", Read(Path.Combine(data, "track_common.rho")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CommitRefusesToRecreateAMissingBackupForManagedData()
    {
        string root = CreateRoot();
        string data = Path.Combine(root, "Data");
        string generated = Path.Combine(root, "generated");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(generated);
        try
        {
            Write(data, "track_common.rho", "original-track");
            Write(data, "aaa.pk", "original-metadata");
            Dictionary<string, string> replacements = CreateReplacements(generated);

            using P236L1DataTransaction transaction = P236L1DataTransaction.Acquire(data);
            transaction.Commit(replacements, recipeVersion: 1, progress: null);
            string backup = Path.Combine(data, P236L1DataTransaction.BackupDirectoryName);
            Directory.Delete(backup, recursive: true);

            Assert.Throws<InvalidDataException>(() =>
                transaction.Commit(replacements, recipeVersion: 1, progress: null));
            Assert.False(Directory.Exists(backup));
            Assert.True(File.Exists(
                Path.Combine(data, P236L1DataTransaction.InstallFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CommitRefusesAStaleBackupAfterRestore()
    {
        string root = CreateRoot();
        string data = Path.Combine(root, "Data");
        string generated = Path.Combine(root, "generated");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(generated);
        try
        {
            Write(data, "track_common.rho", "original-track");
            Write(data, "aaa.pk", "original-metadata");
            Dictionary<string, string> replacements = CreateReplacements(generated);

            using P236L1DataTransaction transaction = P236L1DataTransaction.Acquire(data);
            transaction.Commit(replacements, recipeVersion: 1, progress: null);
            transaction.Restore(progress: null);
            Write(data, "track_common.rho", "new-unmanaged-original");

            Assert.Throws<InvalidDataException>(() =>
                transaction.Commit(replacements, recipeVersion: 1, progress: null));
            Assert.Equal(
                "new-unmanaged-original",
                Read(Path.Combine(data, "track_common.rho")));
            Assert.False(File.Exists(
                Path.Combine(data, P236L1DataTransaction.InstallFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CommittedMarkerWithoutTransactionDirectoryIsCleaned()
    {
        string root = CreateRoot();
        string data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        try
        {
            string transactionId = Guid.NewGuid().ToString("N");
            File.WriteAllBytes(
                Path.Combine(data, P236L1DataTransaction.MarkerFileName),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1,
                    transactionId,
                    phase = "Committed",
                }));

            using P236L1DataTransaction transaction = P236L1DataTransaction.Acquire(data);
            Assert.True(transaction.RecoverIfNeeded());
            Assert.False(File.Exists(
                Path.Combine(data, P236L1DataTransaction.MarkerFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreparedRecoveryPreservesAnExternalLiveReplacement()
    {
        string root = CreateRoot();
        string data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        try
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string transactionDirectory = Path.Combine(
                data,
                ".p236-l1-data-txn-" + transactionId);
            string originalDirectory = Path.Combine(transactionDirectory, "original");
            Directory.CreateDirectory(originalDirectory);
            string originalPath = Write(originalDirectory, "track_common.rho", "original");
            string originalHash = Hash(originalPath);
            string replacementHash = Hash(Write(root, "replacement.rho", "replacement"));
            Write(data, "track_common.rho", "external-change");
            File.WriteAllBytes(
                Path.Combine(transactionDirectory, "transaction.json"),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1,
                    transactionId,
                    files = new[]
                    {
                        new
                        {
                            fileName = "track_common.rho",
                            originalExisted = true,
                            originalSha256 = originalHash,
                            delete = false,
                            replacementSha256 = replacementHash,
                        },
                    },
                }));
            File.WriteAllBytes(
                Path.Combine(data, P236L1DataTransaction.MarkerFileName),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 1,
                    transactionId,
                    phase = "Prepared",
                }));

            using P236L1DataTransaction transaction = P236L1DataTransaction.Acquire(data);
            Assert.Throws<InvalidDataException>(() => transaction.RecoverIfNeeded());
            Assert.Equal("external-change", Read(Path.Combine(data, "track_common.rho")));
            Assert.True(File.Exists(
                Path.Combine(data, P236L1DataTransaction.MarkerFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void L1AndItemProbabilityTransactionsShareTheDataMutationLock()
    {
        string root = CreateRoot();
        string data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        try
        {
            using P236ItemProbabilityTransaction itemTransaction =
                P236ItemProbabilityTransaction.Acquire(data);
            Assert.Throws<IOException>(() => P236L1DataTransaction.Acquire(data));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot() => Path.Combine(
        Path.GetTempPath(),
        "KartRider.P236.L1DataTransactionTests",
        Guid.NewGuid().ToString("N"));

    private static Dictionary<string, string> CreateReplacements(string generated) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["track_common.rho"] = Write(generated, "track_common.rho", "patched-track"),
            ["aaa.pk"] = Write(generated, "aaa.pk", "patched-metadata"),
        };

    private static string Write(string directory, string name, string value)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, value, Encoding.UTF8);
        return path;
    }

    private static string Read(string path) => File.ReadAllText(path, Encoding.UTF8);

    private static string Hash(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}
