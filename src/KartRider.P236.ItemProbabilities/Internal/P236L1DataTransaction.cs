using System.Security.Cryptography;
using System.Text.Json;

namespace KartRider.P236.ItemProbabilities.Internal;

internal sealed record P236L1DataCommitResult(
    int ChangedFileCount,
    string? BackupDirectory);

internal sealed class P236L1DataTransaction : IDisposable
{
    internal const string MarkerFileName = ".p236-l1-data.recovery.json";
    internal const string LockFileName = P236DataMutationLock.FileName;
    internal const string InstallFileName = ".p236-l1-data.install.json";
    internal const string BackupDirectoryName = ".p236-l1-data-backup-v1";
    private const string TransactionPrefix = ".p236-l1-data-txn-";
    private const string TemporaryBackupPrefix = ".p236-l1-data-backup-v1.tmp-";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _dataDirectory;
    private readonly FileStream _lock;
    private bool _disposed;

    private P236L1DataTransaction(string dataDirectory, FileStream transactionLock)
    {
        _dataDirectory = dataDirectory;
        _lock = transactionLock;
    }

    internal static P236L1DataTransaction Acquire(string dataDirectory)
    {
        string fullDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(dataDirectory));
        Directory.CreateDirectory(fullDirectory);
        try
        {
            FileStream transactionLock = new(
                Path.Combine(fullDirectory, LockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.WriteThrough);
            return new P236L1DataTransaction(fullDirectory, transactionLock);
        }
        catch (IOException exception)
        {
            throw new IOException(
                "다른 L1 클라이언트 데이터 작업이 진행 중입니다.",
                exception);
        }
    }

    internal bool RecoverIfNeeded()
    {
        ThrowIfDisposed();
        string markerPath = Path.Combine(_dataDirectory, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        RecoveryMarker marker = ReadJson<RecoveryMarker>(
            markerPath,
            "L1 data recovery marker");
        if (marker.Version != 1 ||
            !Guid.TryParseExact(marker.TransactionId, "N", out Guid transactionId) ||
            marker.Phase is not ("Prepared" or "Committed"))
        {
            throw new InvalidDataException(
                $"L1 데이터 복구 마커가 올바르지 않아 보존했습니다: {markerPath}");
        }

        string transactionDirectory = GetTransactionDirectory(transactionId);
        if (marker.Phase == "Committed")
        {
            // The live file set is already complete. Remove the durable marker
            // before best-effort cleanup so a crash cannot leave a marker that
            // points at a partially removed transaction directory.
            File.Delete(markerPath);
            TryDeleteTransactionDirectory(transactionDirectory);
            return true;
        }

        string manifestPath = Path.Combine(transactionDirectory, "transaction.json");
        TransactionManifest manifest = ReadJson<TransactionManifest>(
            manifestPath,
            "L1 data transaction manifest");
        ValidateTransactionManifest(manifest, transactionId);

        string originalDirectory = Path.Combine(transactionDirectory, "original");
        foreach (TransactionFile file in manifest.Files.Where(file => file.OriginalExisted))
        {
            RequireHash(
                Path.Combine(originalDirectory, file.FileName),
                file.OriginalSha256,
                "transaction snapshot");
        }

        // Validate the entire live set before changing any file. A user or
        // another tool may have replaced a file after the interrupted run.
        foreach (TransactionFile file in manifest.Files)
        {
            RequireRecoverableLiveState(
                Path.Combine(_dataDirectory, file.FileName),
                file);
        }

        foreach (TransactionFile file in manifest.Files)
        {
            string livePath = Path.Combine(_dataDirectory, file.FileName);
            RequireRecoverableLiveState(livePath, file);
            if (file.OriginalExisted)
            {
                string originalPath = Path.Combine(originalDirectory, file.FileName);
                RequireHash(originalPath, file.OriginalSha256, "transaction snapshot");
                AtomicCopy(
                    originalPath,
                    livePath,
                    file.OriginalSha256!,
                    "transaction snapshot",
                    () => RequireRecoverableLiveState(livePath, file));
            }
            else if (File.Exists(livePath))
            {
                File.Delete(livePath);
            }
        }

        File.Delete(markerPath);
        DeleteTransactionDirectory(transactionDirectory);
        return true;
    }

    internal P236L1DataCommitResult Commit(
        IReadOnlyDictionary<string, string> replacementPaths,
        int recipeVersion,
        IProgress<string>? progress)
    {
        ThrowIfDisposed();
        if (replacementPaths.Count == 0)
        {
            return new P236L1DataCommitResult(0, null);
        }

        foreach ((string fileName, string sourcePath) in replacementPaths)
        {
            ValidateDataFileName(fileName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    $"Generated replacement is missing for '{fileName}'.",
                    sourcePath);
            }
        }

        string[] replacementFileNames = replacementPaths.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string backupDirectory = Path.Combine(_dataDirectory, BackupDirectoryName);
        string liveInstallPath = Path.Combine(_dataDirectory, InstallFileName);
        bool backupExists = Directory.Exists(backupDirectory);
        bool installExists = File.Exists(liveInstallPath);
        if (installExists && !backupExists)
        {
            throw new InvalidDataException(
                "L1 데이터 설치 기록은 있지만 원본 백업이 없습니다. 자동으로 새 백업을 만들지 않았습니다.");
        }

        BackupManifest backup = backupExists
            ? LoadBackupManifest(backupDirectory)
            : LoadOrCreatePermanentBackup(replacementFileNames);
        RequireSameFileSet(
            replacementFileNames,
            backup.Files.Select(file => file.FileName),
            "기존 L1 데이터 백업의 파일 집합이 현재 레시피와 일치하지 않습니다.");

        InstallManifest? existingInstall = null;
        string? existingInstallSha256 = null;
        if (installExists)
        {
            (InstallManifest manifest, string sha256) = ReadJsonWithHash<InstallManifest>(
                liveInstallPath,
                "L1 data install manifest");
            existingInstall = manifest;
            existingInstallSha256 = sha256;
            ValidateInstallManifest(existingInstall);
            RequireSameFileSet(
                replacementFileNames,
                existingInstall.Files.Select(file => file.FileName),
                "L1 데이터 설치 기록의 파일 집합이 현재 레시피와 일치하지 않습니다.");
            foreach (InstallFile file in existingInstall.Files)
            {
                RequireHash(
                    Path.Combine(_dataDirectory, file.FileName),
                    file.PatchedSha256,
                    "installed L1 data");
            }
        }
        else if (backupExists)
        {
            // A retained one-time backup may only be reused after Restore when
            // the live files still exactly match that backed-up original set.
            RequireLiveMatchesBackup(backup);
        }

        List<InstallFile> installFiles = replacementPaths
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new InstallFile
            {
                FileName = pair.Key,
                PatchedSha256 = ComputeSha256(pair.Value),
            })
            .ToList();
        InstallManifest install = new()
        {
            Version = 1,
            RecipeVersion = recipeVersion,
            InstalledUtc = DateTimeOffset.UtcNow,
            Files = installFiles,
        };

        string generatedDirectory = Path.GetDirectoryName(
            Path.GetFullPath(replacementPaths.First().Value))
            ?? throw new InvalidOperationException("Generated data has no parent directory.");
        string installSource = Path.Combine(generatedDirectory, InstallFileName);
        string installSourceSha256;
        if (existingInstall is not null)
        {
            if (InstallMatches(existingInstall, install))
            {
                CopyAndFlush(liveInstallPath, installSource, overwrite: true);
                RequireHash(
                    installSource,
                    existingInstallSha256,
                    "existing L1 data install manifest");
                installSourceSha256 = existingInstallSha256!;
            }
            else
            {
                WriteJsonFile(installSource, install);
                installSourceSha256 = ComputeSha256(installSource);
            }
        }
        else
        {
            WriteJsonFile(installSource, install);
            installSourceSha256 = ComputeSha256(installSource);
        }

        Dictionary<string, BackupFile> backupByName = backup.Files.ToDictionary(
            file => file.FileName,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, InstallFile> desiredByName = install.Files.ToDictionary(
            file => file.FileName,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, InstallFile>? existingByName = existingInstall?.Files.ToDictionary(
            file => file.FileName,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, RequestedOperation> operations =
            new(StringComparer.OrdinalIgnoreCase);
        foreach ((string fileName, string replacementPath) in replacementPaths)
        {
            BackupFile original = backupByName[fileName];
            string? expectedLiveHash = existingByName is not null
                ? existingByName[fileName].PatchedSha256
                : original.OriginalSha256;
            operations[fileName] = new RequestedOperation(
                replacementPath,
                desiredByName[fileName].PatchedSha256,
                existingByName is not null || original.Existed,
                expectedLiveHash);
        }

        operations[InstallFileName] = new RequestedOperation(
            installSource,
            installSourceSha256,
            existingInstall is not null,
            existingInstallSha256);

        int changed = CommitOperations(operations, progress);
        return new P236L1DataCommitResult(
            changed,
            Path.Combine(_dataDirectory, BackupDirectoryName));
    }

    internal P236L1DataCommitResult Restore(IProgress<string>? progress)
    {
        ThrowIfDisposed();
        string backupDirectory = Path.Combine(_dataDirectory, BackupDirectoryName);
        BackupManifest backup = LoadBackupManifest(backupDirectory);
        string installPath = Path.Combine(_dataDirectory, InstallFileName);
        if (!File.Exists(installPath))
        {
            throw new InvalidOperationException(
                "관리되는 L1 데이터 설치 기록이 없어 자동 복원할 수 없습니다.");
        }

        (InstallManifest install, string installSha256) = ReadJsonWithHash<InstallManifest>(
            installPath,
            "L1 data install manifest");
        ValidateInstallManifest(install);
        Dictionary<string, InstallFile> installedByName = install.Files.ToDictionary(
            file => file.FileName,
            StringComparer.OrdinalIgnoreCase);
        if (!backup.Files.Select(file => file.FileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(
                    installedByName.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "L1 데이터 백업과 설치 기록의 파일 집합이 일치하지 않습니다.");
        }

        foreach (InstallFile file in install.Files)
        {
            RequireHash(
                Path.Combine(_dataDirectory, file.FileName),
                file.PatchedSha256,
                "installed L1 data");
        }

        Dictionary<string, RequestedOperation> operations =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (BackupFile file in backup.Files)
        {
            operations[file.FileName] = new RequestedOperation(
                file.Existed
                    ? Path.Combine(backupDirectory, "original", file.FileName)
                    : null,
                file.OriginalSha256,
                ExpectedLiveExisted: true,
                installedByName[file.FileName].PatchedSha256);
        }

        operations[InstallFileName] = new RequestedOperation(
            ReplacementPath: null,
            ExpectedReplacementSha256: null,
            ExpectedLiveExisted: true,
            installSha256);
        int changed = CommitOperations(operations, progress);
        return new P236L1DataCommitResult(changed, backupDirectory);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lock.Dispose();
    }

    private int CommitOperations(
        IReadOnlyDictionary<string, RequestedOperation> requestedOperations,
        IProgress<string>? progress)
    {
        List<PendingOperation> operations = new();
        foreach ((string fileName, RequestedOperation request) in requestedOperations)
        {
            ValidateOperationFileName(fileName);
            string livePath = Path.Combine(_dataDirectory, fileName);
            RequireLiveState(
                livePath,
                request.ExpectedLiveExisted,
                request.ExpectedLiveSha256,
                $"pre-transaction '{fileName}'");
            bool delete = request.ReplacementPath is null;
            if (delete && !request.ExpectedLiveExisted)
            {
                continue;
            }

            if (!delete)
            {
                ValidateSha256(
                    request.ExpectedReplacementSha256,
                    $"replacement '{fileName}'");
                if (!File.Exists(request.ReplacementPath))
                {
                    throw new FileNotFoundException(
                        $"Generated replacement is missing for '{fileName}'.",
                        request.ReplacementPath);
                }

                if (request.ExpectedLiveExisted &&
                    string.Equals(
                        request.ExpectedLiveSha256,
                        request.ExpectedReplacementSha256,
                        StringComparison.Ordinal))
                {
                    continue;
                }
            }

            operations.Add(new PendingOperation(
                fileName,
                request.ReplacementPath,
                request.ExpectedReplacementSha256,
                request.ExpectedLiveExisted,
                request.ExpectedLiveSha256,
                delete));
        }

        if (operations.Count == 0)
        {
            progress?.Report("L1 클라이언트 데이터가 이미 요청한 상태입니다.");
            return 0;
        }

        Guid transactionId = Guid.NewGuid();
        string transactionDirectory = GetTransactionDirectory(transactionId);
        string originalDirectory = Path.Combine(transactionDirectory, "original");
        string replacementDirectory = Path.Combine(transactionDirectory, "replacement");
        Directory.CreateDirectory(originalDirectory);
        Directory.CreateDirectory(replacementDirectory);
        bool markerWritten = false;
        try
        {
            List<TransactionFile> transactionFiles = new();
            foreach (PendingOperation operation in operations)
            {
                string livePath = Path.Combine(_dataDirectory, operation.FileName);
                bool existed = File.Exists(livePath);
                string? originalSha256 = null;
                if (existed)
                {
                    string snapshotPath = Path.Combine(originalDirectory, operation.FileName);
                    CopyAndFlush(livePath, snapshotPath, overwrite: false);
                    originalSha256 = ComputeSha256(snapshotPath);
                }

                RequireSnapshotMatchesExpected(
                    operation,
                    existed,
                    originalSha256);

                string? replacementSha256 = null;
                if (!operation.Delete)
                {
                    string stagedPath = Path.Combine(replacementDirectory, operation.FileName);
                    CopyAndFlush(operation.ReplacementPath!, stagedPath, overwrite: false);
                    replacementSha256 = ComputeSha256(stagedPath);
                    if (!string.Equals(
                            replacementSha256,
                            operation.ExpectedReplacementSha256,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Replacement changed while staging '{operation.FileName}'.");
                    }
                }

                transactionFiles.Add(new TransactionFile
                {
                    FileName = operation.FileName,
                    OriginalExisted = existed,
                    OriginalSha256 = originalSha256,
                    Delete = operation.Delete,
                    ReplacementSha256 = replacementSha256,
                });
            }

            TransactionManifest manifest = new()
            {
                Version = 1,
                TransactionId = transactionId.ToString("N"),
                Files = transactionFiles,
            };
            WriteJsonFile(Path.Combine(transactionDirectory, "transaction.json"), manifest);

            // Refuse to overwrite a file that changed after the snapshot.
            foreach (TransactionFile file in transactionFiles)
            {
                string livePath = Path.Combine(_dataDirectory, file.FileName);
                if (file.OriginalExisted)
                {
                    RequireHash(livePath, file.OriginalSha256, "live pre-commit data");
                }
                else if (File.Exists(livePath))
                {
                    throw new IOException(
                        $"'{file.FileName}' appeared while the L1 data transaction was being prepared.");
                }
            }

            WriteMarker(transactionId, "Prepared");
            markerWritten = true;
            foreach (TransactionFile file in OrderForCommit(transactionFiles))
            {
                progress?.Report($"L1 데이터 교체: {file.FileName}");
                string livePath = Path.Combine(_dataDirectory, file.FileName);
                RequireLiveState(
                    livePath,
                    file.OriginalExisted,
                    file.OriginalSha256,
                    $"live pre-replace '{file.FileName}'");
                if (file.Delete)
                {
                    if (File.Exists(livePath))
                    {
                        File.Delete(livePath);
                    }
                }
                else
                {
                    string stagedPath = Path.Combine(replacementDirectory, file.FileName);
                    File.Move(stagedPath, livePath, overwrite: true);
                    RequireHash(livePath, file.ReplacementSha256, "committed L1 data");
                }
            }

            // Include requested no-op files in the final validation. This
            // catches an external write that raced with a multi-file commit.
            foreach ((string fileName, RequestedOperation request) in requestedOperations)
            {
                RequireLiveState(
                    Path.Combine(_dataDirectory, fileName),
                    request.ReplacementPath is not null,
                    request.ExpectedReplacementSha256,
                    $"committed '{fileName}'");
            }

            WriteMarker(transactionId, "Committed");
            TryCleanupCommitted(transactionDirectory);
            return transactionFiles.Count;
        }
        catch
        {
            if (markerWritten)
            {
                try
                {
                    RecoverIfNeeded();
                }
                catch
                {
                    // Preserve the original exception. The marker remains for
                    // a later explicit recovery attempt if rollback also failed.
                }
            }
            else
            {
                TryDeleteTransactionDirectory(transactionDirectory);
            }

            throw;
        }
    }

    private BackupManifest LoadOrCreatePermanentBackup(IEnumerable<string> fileNames)
    {
        string backupDirectory = Path.Combine(_dataDirectory, BackupDirectoryName);
        if (Directory.Exists(backupDirectory))
        {
            BackupManifest existing = LoadBackupManifest(backupDirectory);
            string[] expected = fileNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            string[] actual = existing.Files.Select(file => file.FileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "기존 L1 데이터 백업의 파일 집합이 현재 레시피와 일치하지 않습니다.");
            }

            return existing;
        }

        if (File.Exists(backupDirectory))
        {
            throw new InvalidDataException(
                $"L1 data backup path is not a directory: {backupDirectory}");
        }

        string temporaryDirectory = Path.Combine(
            _dataDirectory,
            TemporaryBackupPrefix + Guid.NewGuid().ToString("N"));
        string originalDirectory = Path.Combine(temporaryDirectory, "original");
        Directory.CreateDirectory(originalDirectory);
        try
        {
            BackupManifest manifest = new()
            {
                Version = 1,
                CreatedUtc = DateTimeOffset.UtcNow,
            };
            foreach (string fileName in fileNames)
            {
                ValidateDataFileName(fileName);
                string livePath = Path.Combine(_dataDirectory, fileName);
                bool existed = File.Exists(livePath);
                string? hash = null;
                if (existed)
                {
                    string backupPath = Path.Combine(originalDirectory, fileName);
                    CopyAndFlush(livePath, backupPath, overwrite: false);
                    hash = ComputeSha256(backupPath);
                }

                manifest.Files.Add(new BackupFile
                {
                    FileName = fileName,
                    Existed = existed,
                    OriginalSha256 = hash,
                });
            }

            WriteJsonFile(Path.Combine(temporaryDirectory, "manifest.json"), manifest);
            Directory.Move(temporaryDirectory, backupDirectory);
            return manifest;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private void RequireLiveMatchesBackup(BackupManifest backup)
    {
        foreach (BackupFile file in backup.Files)
        {
            string livePath = Path.Combine(_dataDirectory, file.FileName);
            if (file.Existed)
            {
                RequireHash(livePath, file.OriginalSha256, "backed-up original L1 data");
            }
            else if (File.Exists(livePath))
            {
                throw new InvalidDataException(
                    $"원래 없었던 '{file.FileName}' 파일이 복원 후 생성되어 기존 백업을 재사용할 수 없습니다.");
            }
        }
    }

    private static void RequireSameFileSet(
        IEnumerable<string> expectedNames,
        IEnumerable<string> actualNames,
        string message)
    {
        string[] expected = expectedNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] actual = actualNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(message);
        }
    }

    private BackupManifest LoadBackupManifest(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            throw new DirectoryNotFoundException(
                $"L1 데이터 원본 백업이 없습니다: {backupDirectory}");
        }

        BackupManifest manifest = ReadJson<BackupManifest>(
            Path.Combine(backupDirectory, "manifest.json"),
            "L1 data backup manifest");
        if (manifest.Version != 1 || manifest.Files.Count == 0)
        {
            throw new InvalidDataException("L1 data backup manifest is invalid.");
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (BackupFile file in manifest.Files)
        {
            ValidateDataFileName(file.FileName);
            if (!names.Add(file.FileName))
            {
                throw new InvalidDataException(
                    $"L1 data backup contains duplicate file '{file.FileName}'.");
            }

            if (file.Existed)
            {
                RequireHash(
                    Path.Combine(backupDirectory, "original", file.FileName),
                    file.OriginalSha256,
                    "permanent L1 data backup");
            }
            else if (file.OriginalSha256 is not null)
            {
                throw new InvalidDataException(
                    $"Absent backup file '{file.FileName}' unexpectedly has a hash.");
            }
        }

        return manifest;
    }

    private static void ValidateInstallManifest(InstallManifest manifest)
    {
        if (manifest.Version != 1 || manifest.RecipeVersion <= 0 || manifest.Files.Count == 0)
        {
            throw new InvalidDataException("L1 data install manifest is invalid.");
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstallFile file in manifest.Files)
        {
            ValidateDataFileName(file.FileName);
            ValidateSha256(file.PatchedSha256, $"installed file '{file.FileName}'");
            if (!names.Add(file.FileName))
            {
                throw new InvalidDataException(
                    $"L1 data install contains duplicate file '{file.FileName}'.");
            }
        }
    }

    private static bool InstallMatches(InstallManifest left, InstallManifest right)
    {
        if (left.Version != right.Version ||
            left.RecipeVersion != right.RecipeVersion ||
            left.Files.Count != right.Files.Count)
        {
            return false;
        }

        Dictionary<string, string> leftHashes = left.Files.ToDictionary(
            file => file.FileName,
            file => file.PatchedSha256,
            StringComparer.OrdinalIgnoreCase);
        return right.Files.All(file =>
            leftHashes.TryGetValue(file.FileName, out string? hash) &&
            string.Equals(hash, file.PatchedSha256, StringComparison.Ordinal));
    }

    private static IEnumerable<TransactionFile> OrderForCommit(
        IEnumerable<TransactionFile> files) =>
        files.OrderBy(file =>
            string.Equals(file.FileName, InstallFileName, StringComparison.OrdinalIgnoreCase) ? 2 :
            string.Equals(file.FileName, "aaa.pk", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase);

    private void WriteMarker(Guid transactionId, string phase)
    {
        RecoveryMarker marker = new()
        {
            Version = 1,
            TransactionId = transactionId.ToString("N"),
            Phase = phase,
        };
        WriteJsonFile(Path.Combine(_dataDirectory, MarkerFileName), marker);
    }

    private string GetTransactionDirectory(Guid transactionId) =>
        Path.Combine(
            _dataDirectory,
            TransactionPrefix + transactionId.ToString("N"));

    private static void ValidateTransactionManifest(
        TransactionManifest manifest,
        Guid expectedId)
    {
        if (manifest.Version != 1 ||
            !string.Equals(
                manifest.TransactionId,
                expectedId.ToString("N"),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("L1 data transaction manifest is invalid.");
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (TransactionFile file in manifest.Files)
        {
            ValidateOperationFileName(file.FileName);
            if (!names.Add(file.FileName))
            {
                throw new InvalidDataException(
                    $"L1 data transaction contains duplicate file '{file.FileName}'.");
            }

            if (file.OriginalExisted)
            {
                ValidateSha256(file.OriginalSha256, $"original '{file.FileName}'");
            }
            else if (file.OriginalSha256 is not null)
            {
                throw new InvalidDataException(
                    $"Absent transaction file '{file.FileName}' unexpectedly has an original hash.");
            }

            if (file.Delete)
            {
                if (file.ReplacementSha256 is not null)
                {
                    throw new InvalidDataException(
                        $"Deleted transaction file '{file.FileName}' unexpectedly has a replacement hash.");
                }
            }
            else
            {
                ValidateSha256(file.ReplacementSha256, $"replacement '{file.FileName}'");
            }
        }
    }

    private static void ValidateDataFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !(string.Equals(fileName, "aaa.pk", StringComparison.OrdinalIgnoreCase) ||
              fileName.EndsWith(".rho", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Invalid L1 data file name '{fileName}'.");
        }
    }

    private static void ValidateOperationFileName(string fileName)
    {
        if (string.Equals(fileName, InstallFileName, StringComparison.Ordinal))
        {
            return;
        }

        ValidateDataFileName(fileName);
    }

    private static void ValidateSha256(string? hash, string context)
    {
        if (hash is null || hash.Length != 64 ||
            !hash.All(character =>
                character is >= '0' and <= '9' or >= 'A' and <= 'F'))
        {
            throw new InvalidDataException($"Invalid SHA-256 for {context}.");
        }
    }

    private static void RequireHash(string path, string? expected, string context)
    {
        ValidateSha256(expected, context);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing {context}: {path}", path);
        }

        string actual = ComputeSha256(path);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"SHA-256 mismatch for {context}: {path}");
        }
    }

    private static void RequireLiveState(
        string path,
        bool expectedExisted,
        string? expectedSha256,
        string context)
    {
        if (expectedExisted)
        {
            RequireHash(path, expectedSha256, context);
            return;
        }

        if (expectedSha256 is not null)
        {
            throw new InvalidDataException(
                $"Absent {context} unexpectedly has a SHA-256 value.");
        }

        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new InvalidDataException(
                $"Expected {context} to be absent: {path}");
        }
    }

    private static void RequireRecoverableLiveState(
        string livePath,
        TransactionFile file)
    {
        if (Directory.Exists(livePath))
        {
            throw new InvalidDataException(
                $"Interrupted L1 data file was replaced by a directory: {livePath}");
        }

        if (!File.Exists(livePath))
        {
            if (!file.OriginalExisted || file.Delete)
            {
                return;
            }

            throw new InvalidDataException(
                $"Interrupted L1 data file disappeared outside the transaction: {livePath}");
        }

        string actual = ComputeSha256(livePath);
        bool isOriginal = file.OriginalExisted && string.Equals(
            actual,
            file.OriginalSha256,
            StringComparison.Ordinal);
        bool isReplacement = !file.Delete && string.Equals(
            actual,
            file.ReplacementSha256,
            StringComparison.Ordinal);
        if (!isOriginal && !isReplacement)
        {
            throw new InvalidDataException(
                $"Interrupted L1 data changed outside the transaction and was preserved: {livePath}");
        }
    }

    private static void RequireSnapshotMatchesExpected(
        PendingOperation operation,
        bool snapshotExisted,
        string? snapshotSha256)
    {
        if (snapshotExisted != operation.ExpectedLiveExisted)
        {
            throw new IOException(
                $"'{operation.FileName}' changed existence while the L1 transaction was staged.");
        }

        if (snapshotExisted && !string.Equals(
                snapshotSha256,
                operation.ExpectedLiveSha256,
                StringComparison.Ordinal))
        {
            throw new IOException(
                $"'{operation.FileName}' changed while the L1 transaction was staged.");
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static T ReadJson<T>(string path, string context)
    {
        return ReadJsonWithHash<T>(path, context).Value;
    }

    private static (T Value, string Sha256) ReadJsonWithHash<T>(
        string path,
        string context)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            T value = JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidDataException($"{context} is null: {path}");
            return (value, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"{context} is malformed and was preserved: {path}",
                exception);
        }
    }

    private static void WriteJsonFile<T>(string path, T value)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("JSON output has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       16 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void AtomicCopy(
        string sourcePath,
        string destinationPath,
        string expectedSha256,
        string context,
        Action beforeCommit)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Destination has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".p236-l1-data-recovery-{Guid.NewGuid():N}.tmp");
        try
        {
            CopyAndFlush(sourcePath, temporaryPath, overwrite: false);
            RequireHash(temporaryPath, expectedSha256, context);
            beforeCommit();
            File.Move(temporaryPath, destinationPath, overwrite: true);
            RequireHash(destinationPath, expectedSha256, context);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void CopyAndFlush(
        string sourcePath,
        string destinationPath,
        bool overwrite)
    {
        using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        using FileStream destination = new(
            destinationPath,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.WriteThrough);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    private static void DeleteTransactionDirectory(string transactionDirectory)
    {
        string fullPath = Path.GetFullPath(transactionDirectory);
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        if (!name.StartsWith(TransactionPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[TransactionPrefix.Length..], "N", out _))
        {
            throw new InvalidOperationException(
                "Refusing to delete an invalid L1 data transaction directory.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private static void TryDeleteTransactionDirectory(string transactionDirectory)
    {
        try
        {
            DeleteTransactionDirectory(transactionDirectory);
        }
        catch (IOException)
        {
            // No marker means no live file was changed.
        }
        catch (UnauthorizedAccessException)
        {
            // No marker means no live file was changed.
        }
    }

    private void TryCleanupCommitted(string transactionDirectory)
    {
        try
        {
            File.Delete(Path.Combine(_dataDirectory, MarkerFileName));
            DeleteTransactionDirectory(transactionDirectory);
        }
        catch (IOException)
        {
            // The committed marker makes cleanup safely retryable.
        }
        catch (UnauthorizedAccessException)
        {
            // The committed marker makes cleanup safely retryable.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record RequestedOperation(
        string? ReplacementPath,
        string? ExpectedReplacementSha256,
        bool ExpectedLiveExisted,
        string? ExpectedLiveSha256);

    private sealed record PendingOperation(
        string FileName,
        string? ReplacementPath,
        string? ExpectedReplacementSha256,
        bool ExpectedLiveExisted,
        string? ExpectedLiveSha256,
        bool Delete);

    private sealed class RecoveryMarker
    {
        public int Version { get; set; }

        public string TransactionId { get; set; } = string.Empty;

        public string Phase { get; set; } = string.Empty;
    }

    private sealed class TransactionManifest
    {
        public int Version { get; set; }

        public string TransactionId { get; set; } = string.Empty;

        public List<TransactionFile> Files { get; set; } = new();
    }

    private sealed class TransactionFile
    {
        public string FileName { get; set; } = string.Empty;

        public bool OriginalExisted { get; set; }

        public string? OriginalSha256 { get; set; }

        public bool Delete { get; set; }

        public string? ReplacementSha256 { get; set; }
    }

    private sealed class BackupManifest
    {
        public int Version { get; set; }

        public DateTimeOffset CreatedUtc { get; set; }

        public List<BackupFile> Files { get; set; } = new();
    }

    private sealed class BackupFile
    {
        public string FileName { get; set; } = string.Empty;

        public bool Existed { get; set; }

        public string? OriginalSha256 { get; set; }
    }

    private sealed class InstallManifest
    {
        public int Version { get; set; }

        public int RecipeVersion { get; set; }

        public DateTimeOffset InstalledUtc { get; set; }

        public List<InstallFile> Files { get; set; } = new();
    }

    private sealed class InstallFile
    {
        public string FileName { get; set; } = string.Empty;

        public string PatchedSha256 { get; set; } = string.Empty;
    }
}
