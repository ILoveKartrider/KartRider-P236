using System.Text.Json;

namespace KartRider.P236.ItemProbabilities.Internal;

internal enum ItemProbabilityTransactionPhase
{
    Prepared,
    ItemReplaced,
    Committed,
}

internal sealed class P236ItemProbabilityTransaction : IDisposable
{
    internal const string MarkerFileName = ".p236-item-probabilities.recovery.json";
    internal const string LockFileName = P236DataMutationLock.FileName;
    internal const string ItemBackupFileName = "item.rho.p236-item-probabilities.bak";
    internal const string MetadataBackupFileName = "aaa.pk.p236-item-probabilities.bak";
    private const string TransactionPrefix = ".p236-item-probabilities-txn-";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _dataDirectory;
    private readonly FileStream _lock;
    private bool _disposed;

    private P236ItemProbabilityTransaction(string dataDirectory, FileStream transactionLock)
    {
        _dataDirectory = dataDirectory;
        _lock = transactionLock;
    }

    internal static P236ItemProbabilityTransaction Acquire(string dataDirectory)
    {
        string fullDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(fullDirectory);
        string lockPath = Path.Combine(fullDirectory, LockFileName);
        try
        {
            FileStream transactionLock = new(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            return new P236ItemProbabilityTransaction(fullDirectory, transactionLock);
        }
        catch (IOException exception)
        {
            throw new IOException("Another item probability import/apply operation is active.", exception);
        }
    }

    internal bool RecoverIfNeeded()
    {
        ThrowIfDisposed();
        string markerPath = GetMarkerPath(_dataDirectory);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        TransactionMarker marker;
        try
        {
            marker = JsonSerializer.Deserialize<TransactionMarker>(File.ReadAllBytes(markerPath), JsonOptions)
                ?? throw new InvalidDataException("Item probability recovery marker is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Item probability recovery marker is malformed and was preserved: {markerPath}",
                exception);
        }

        if (marker.Version != 1 ||
            !Guid.TryParseExact(marker.TransactionId, "N", out Guid transactionId) ||
            !Enum.TryParse(marker.Phase, ignoreCase: false, out ItemProbabilityTransactionPhase phase))
        {
            throw new InvalidDataException(
                $"Item probability recovery marker is invalid and was preserved: {markerPath}");
        }

        string transactionDirectory = GetTransactionDirectory(_dataDirectory, transactionId);
        if (phase != ItemProbabilityTransactionPhase.Committed)
        {
            string originalDirectory = Path.Combine(transactionDirectory, "original");
            string originalItem = Path.Combine(originalDirectory, "item.rho");
            string originalMetadata = Path.Combine(originalDirectory, "aaa.pk");
            if (!File.Exists(originalItem) || !File.Exists(originalMetadata))
            {
                throw new InvalidDataException(
                    $"Recovery files are missing; marker was preserved: {markerPath}");
            }

            AtomicCopy(originalItem, Path.Combine(_dataDirectory, "item.rho"));
            AtomicCopy(originalMetadata, Path.Combine(_dataDirectory, "aaa.pk"));

            // The rollback is complete. Removing the marker first prevents a
            // harmless transaction-directory cleanup failure from causing a
            // later recovery attempt to expect snapshots that were deleted.
            File.Delete(markerPath);
            DeleteTransactionDirectory(transactionDirectory);
            return true;
        }

        // A committed marker is cleanup-only: never roll the live pair back.
        // Keep it until the transaction directory is gone so a cleanup failure
        // remains safely retryable on the next import/apply.
        DeleteTransactionDirectory(transactionDirectory);
        File.Delete(markerPath);
        return true;
    }

    internal TransactionFiles Prepare(string itemPath, string metadataPath)
    {
        ThrowIfDisposed();
        Guid transactionId = Guid.NewGuid();
        string transactionDirectory = GetTransactionDirectory(_dataDirectory, transactionId);
        string originalDirectory = Path.Combine(transactionDirectory, "original");
        string replacementDirectory = Path.Combine(transactionDirectory, "replacement");
        try
        {
            Directory.CreateDirectory(originalDirectory);
            Directory.CreateDirectory(replacementDirectory);

            string originalItem = Path.Combine(originalDirectory, "item.rho");
            string originalMetadata = Path.Combine(originalDirectory, "aaa.pk");
            // Hold read locks on both live files while taking the snapshot. This both
            // rejects an already-active writer and prevents a writer from producing a
            // mixed item.rho/aaa.pk pair between the two copies.
            using (FileStream liveItem = OpenReadLock(itemPath))
            using (FileStream liveMetadata = OpenReadLock(metadataPath))
            {
                CopyAndFlush(liveItem, originalItem);
                CopyAndFlush(liveMetadata, originalMetadata);
            }

            return new TransactionFiles(
                transactionId,
                transactionDirectory,
                originalItem,
                originalMetadata,
                Path.Combine(replacementDirectory, "item.rho"),
                Path.Combine(replacementDirectory, "aaa.pk"));
        }
        catch
        {
            TryDeleteTransactionDirectory(transactionDirectory);
            throw;
        }
    }

    internal void Commit(TransactionFiles files, string itemPath, string metadataPath)
    {
        ThrowIfDisposed();

        // Do not arm recovery until both live files are proven to still be the
        // exact pair captured by Prepare. A mismatch belongs to another writer;
        // in that case there must be no overwrite and no recovery rollback.
        using (FileStream liveItem = OpenReadLock(itemPath))
        using (FileStream liveMetadata = OpenReadLock(metadataPath))
        {
            if (!ContentsEqual(liveItem, files.OriginalItemPath) ||
                !ContentsEqual(liveMetadata, files.OriginalMetadataPath))
            {
                throw new IOException(
                    "item.rho or aaa.pk changed outside this transaction; apply was aborted without overwriting the live files.");
            }

            WriteMarker(files.TransactionId, ItemProbabilityTransactionPhase.Prepared);
        }

        File.Move(files.ReplacementItemPath, itemPath, overwrite: true);
        WriteMarker(files.TransactionId, ItemProbabilityTransactionPhase.ItemReplaced);
        File.Move(files.ReplacementMetadataPath, metadataPath, overwrite: true);
        WriteMarker(files.TransactionId, ItemProbabilityTransactionPhase.Committed);

        // At this point the new live pair is fully committed. Cleanup is allowed
        // to remain pending, but must not make Apply report a failed/rolled-back
        // transaction. The committed marker makes the cleanup retryable.
        TryCleanupCommitted(files.TransactionDirectory);
    }

    internal void AbandonUnmarked(TransactionFiles files)
    {
        if (!File.Exists(GetMarkerPath(_dataDirectory)))
        {
            DeleteTransactionDirectory(files.TransactionDirectory);
        }
    }

    internal static string GetMarkerPath(string dataDirectory) =>
        Path.Combine(Path.GetFullPath(dataDirectory), MarkerFileName);

    internal static void EnsurePermanentBackup(string sourcePath, string backupPath)
    {
        if (File.Exists(backupPath))
        {
            return;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(backupPath))
            ?? throw new InvalidOperationException("Backup has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(backupPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // The durable bytes first land under a disposable name. Only a fully
            // flushed copy can atomically become the permanent one-time backup.
            CopyAndFlush(sourcePath, temporaryPath, overwrite: false);
            try
            {
                File.Move(temporaryPath, backupPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                // Another completed call won the one-time backup race.
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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

    private void WriteMarker(Guid transactionId, ItemProbabilityTransactionPhase phase)
    {
        TransactionMarker marker = new()
        {
            Version = 1,
            TransactionId = transactionId.ToString("N"),
            Phase = phase.ToString(),
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(marker, JsonOptions);
        string markerPath = GetMarkerPath(_dataDirectory);
        string temporaryPath = markerPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, markerPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void AtomicCopy(string sourcePath, string destinationPath)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Destination has no parent directory.");
        string temporaryPath = Path.Combine(directory, $".p236-recovery-{Guid.NewGuid():N}.tmp");
        try
        {
            CopyAndFlush(sourcePath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void CopyAndFlush(string sourcePath, string destinationPath, bool overwrite)
    {
        using FileStream source = OpenReadLock(sourcePath);
        CopyAndFlush(source, destinationPath, overwrite);
    }

    private static void CopyAndFlush(FileStream source, string destinationPath) =>
        CopyAndFlush(source, destinationPath, overwrite: false);

    private static void CopyAndFlush(FileStream source, string destinationPath, bool overwrite)
    {
        source.Position = 0;
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

    private static FileStream OpenReadLock(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1024,
        FileOptions.SequentialScan);

    private static bool ContentsEqual(FileStream live, string snapshotPath)
    {
        using FileStream snapshot = OpenReadLock(snapshotPath);
        if (live.Length != snapshot.Length)
        {
            return false;
        }

        live.Position = 0;
        snapshot.Position = 0;
        Span<byte> liveBuffer = stackalloc byte[16 * 1024];
        Span<byte> snapshotBuffer = stackalloc byte[16 * 1024];
        while (true)
        {
            int liveRead = live.Read(liveBuffer);
            int snapshotRead = snapshot.Read(snapshotBuffer);
            if (liveRead != snapshotRead)
            {
                return false;
            }

            if (liveRead == 0)
            {
                return true;
            }

            if (!liveBuffer[..liveRead].SequenceEqual(snapshotBuffer[..snapshotRead]))
            {
                return false;
            }
        }
    }

    private static string GetTransactionDirectory(string dataDirectory, Guid transactionId) =>
        Path.Combine(Path.GetFullPath(dataDirectory), TransactionPrefix + transactionId.ToString("N"));

    private static void DeleteTransactionDirectory(string transactionDirectory)
    {
        string fullPath = Path.GetFullPath(transactionDirectory);
        string? parent = Path.GetDirectoryName(fullPath);
        string name = Path.GetFileName(fullPath);
        if (parent is null ||
            !name.StartsWith(TransactionPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[TransactionPrefix.Length..], "N", out _))
        {
            throw new InvalidOperationException("Refusing to delete an invalid transaction directory.");
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
            // Best effort for an unmarked transaction; no live file was changed.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort for an unmarked transaction; no live file was changed.
        }
    }

    private void TryCleanupCommitted(string transactionDirectory)
    {
        try
        {
            DeleteTransactionDirectory(transactionDirectory);
            File.Delete(GetMarkerPath(_dataDirectory));
        }
        catch (IOException)
        {
            // Leave the Committed marker for RecoverIfNeeded to retry cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Leave the Committed marker for RecoverIfNeeded to retry cleanup.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal sealed record TransactionFiles(
        Guid TransactionId,
        string TransactionDirectory,
        string OriginalItemPath,
        string OriginalMetadataPath,
        string ReplacementItemPath,
        string ReplacementMetadataPath);

    private sealed class TransactionMarker
    {
        public int Version { get; set; }

        public string TransactionId { get; set; } = string.Empty;

        public string Phase { get; set; } = string.Empty;
    }
}
