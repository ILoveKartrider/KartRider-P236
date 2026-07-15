using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Win32;

namespace KartRider.P236.Connector;

internal sealed record ClientInstanceOption(
    string Name,
    string RootDirectory,
    string ExecutablePath,
    string? Username)
{
    public override string ToString() => $"{Name}  —  {RootDirectory}";
}

internal sealed record ClientInstanceDiscoveryResult(
    IReadOnlyList<ClientInstanceOption> Instances,
    string? PreferredExecutablePath,
    IReadOnlyList<string> Warnings);

internal static class ClientInstanceDiscovery
{
    internal const string CatalogFileName = "connector-instances.json";
    private const int CatalogVersion = 1;
    private const int MaximumCatalogLength = 256 * 1024;
    private const int MaximumEntries = 256;
    private const int MaximumNameLength = 80;
    private const int MaximumPathLength = 1024;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    internal static ClientInstanceDiscoveryResult DiscoverPreparedInstances()
    {
        List<string> warnings = new List<string>();
        List<(string Name, string Root)> registryInstances = ReadRegistryInstances(warnings);
        return DiscoverPreparedInstances(
            AppContext.BaseDirectory,
            registryInstances,
            OriginalClientValidator.IsKnownOriginal,
            OriginalClientValidator.IsKnownConfigurable,
            PreparedClientSettings.RecoverIfNeeded,
            LegacyAccountProfile.TryReadUsername,
            warnings);
    }

    internal static ClientInstanceDiscoveryResult DiscoverPreparedInstances(
        string connectorDirectory,
        IEnumerable<(string Name, string Root)> registryInstances,
        Func<string, bool> isKnownOriginal,
        Func<string, bool> isKnownConfigurable,
        Action<string> recover,
        Func<string, string?> usernameReader,
        ICollection<string>? initialWarnings = null)
    {
        ArgumentNullException.ThrowIfNull(registryInstances);
        ArgumentNullException.ThrowIfNull(isKnownOriginal);
        ArgumentNullException.ThrowIfNull(isKnownConfigurable);
        ArgumentNullException.ThrowIfNull(recover);
        ArgumentNullException.ThrowIfNull(usernameReader);

        string baseDirectory = NormalizeDirectory(connectorDirectory);
        List<string> warnings = initialWarnings?.ToList() ?? new List<string>();
        List<ClientInstanceCandidate> candidates = new List<ClientInstanceCandidate>();
        PortableCatalog? catalog = TryReadCatalog(baseDirectory, warnings);
        if (catalog?.Instances != null)
        {
            foreach (PortableInstanceEntry? entry in catalog.Instances.Take(MaximumEntries))
            {
                if (entry == null)
                {
                    warnings.Add($"{CatalogFileName}의 null 인스턴스 항목을 건너뜁니다.");
                    continue;
                }
                string? root = ResolveStoredPath(baseDirectory, entry.Path);
                if (root != null)
                {
                    candidates.Add(new ClientInstanceCandidate(
                        NormalizeDisplayName(entry.Name, root),
                        root));
                }
            }
        }

        candidates.Add(new ClientInstanceCandidate(
            NormalizeDisplayName(null, baseDirectory),
            baseDirectory));

        string clientsDirectory = Path.Combine(baseDirectory, "clients");
        try
        {
            if (Directory.Exists(clientsDirectory))
            {
                foreach (string directory in Directory.EnumerateDirectories(
                             clientsDirectory,
                             "*",
                             SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(new ClientInstanceCandidate(
                        NormalizeDisplayName(null, directory),
                        directory));
                }
            }
        }
        catch (Exception exception) when (IsDiscoveryException(exception))
        {
            warnings.Add($"clients 폴더를 읽지 못했습니다: {exception.Message}");
        }

        try
        {
            foreach ((string name, string root) in registryInstances)
            {
                candidates.Add(new ClientInstanceCandidate(name, root));
            }
        }
        catch (Exception exception) when (IsDiscoveryException(exception))
        {
            warnings.Add($"레지스트리 인스턴스 목록을 읽지 못했습니다: {exception.Message}");
        }

        Dictionary<string, ClientInstanceOption> instances =
            new Dictionary<string, ClientInstanceOption>(StringComparer.OrdinalIgnoreCase);
        foreach (ClientInstanceCandidate candidate in candidates)
        {
            TryAdd(
                instances,
                candidate,
                isKnownOriginal,
                isKnownConfigurable,
                recover,
                usernameReader);
        }

        string? preferredExecutable = catalog == null
            ? null
            : ResolveStoredPath(baseDirectory, catalog.LastSelectedPath) is string preferredRoot
                ? Path.Combine(preferredRoot, "KartRider.exe")
                : null;
        return new ClientInstanceDiscoveryResult(
            instances.Values
                .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(instance => instance.RootDirectory, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            preferredExecutable,
            warnings.ToArray());
    }

    internal static string RememberInstance(
        string rootDirectory,
        string? displayName = null) =>
        RememberInstance(AppContext.BaseDirectory, rootDirectory, displayName);

    internal static string RememberInstance(
        string connectorDirectory,
        string rootDirectory,
        string? displayName = null)
    {
        string baseDirectory = NormalizeDirectory(connectorDirectory);
        string root = NormalizeDirectory(rootDirectory);
        string executable = Path.Combine(root, "KartRider.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("저장할 인스턴스에 KartRider.exe가 없습니다.", executable);
        }

        string catalogPath = Path.Combine(baseDirectory, CatalogFileName);
        using FileStream catalogLock = AcquireCatalogLock(catalogPath + ".lock");
        PortableCatalog catalog = ReadCatalogForWrite(catalogPath);
        catalog.Instances ??= new List<PortableInstanceEntry?>();
        if (catalog.Instances.Count > MaximumEntries)
        {
            throw new InvalidDataException(
                $"인스턴스 목록은 최대 {MaximumEntries}개까지 저장할 수 있습니다.");
        }

        string storedPath = ToStoredPath(baseDirectory, root);
        if (storedPath.Length > MaximumPathLength)
        {
            throw new InvalidDataException(
                $"인스턴스 경로는 최대 {MaximumPathLength}자까지 저장할 수 있습니다.");
        }

        PortableInstanceEntry? existing = catalog.Instances.FirstOrDefault(entry =>
            entry != null &&
            ResolveStoredPath(baseDirectory, entry.Path) is string existingRoot &&
            string.Equals(existingRoot, root, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            if (catalog.Instances.Count >= MaximumEntries)
            {
                throw new InvalidDataException(
                    $"인스턴스 목록은 최대 {MaximumEntries}개까지 저장할 수 있습니다.");
            }
            existing = new PortableInstanceEntry
            {
                Name = NormalizeDisplayName(displayName, root),
                Path = storedPath
            };
            catalog.Instances.Add(existing);
        }
        else
        {
            existing.Name = NormalizeDisplayName(
                string.IsNullOrWhiteSpace(displayName) ? existing.Name : displayName,
                root);
            existing.Path = storedPath;
        }

        for (int index = catalog.Instances.Count - 1; index >= 0; index--)
        {
            PortableInstanceEntry? entry = catalog.Instances[index];
            if (entry == null || ReferenceEquals(entry, existing))
            {
                continue;
            }
            if (ResolveStoredPath(baseDirectory, entry.Path) is string duplicateRoot &&
                string.Equals(duplicateRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                catalog.Instances.RemoveAt(index);
            }
        }

        catalog.Version = CatalogVersion;
        catalog.LastSelectedPath = storedPath;
        WriteCatalogAtomically(catalogPath, catalog);
        return catalogPath;
    }

    private static List<(string Name, string Root)> ReadRegistryInstances(ICollection<string> warnings)
    {
        List<(string Name, string Root)> result = new List<(string Name, string Root)>();
        try
        {
            using RegistryKey? instances = Registry.CurrentUser.OpenSubKey(@"Software\TCGame\kart\instances");
            if (instances != null)
            {
                foreach (string subKeyName in instances.GetSubKeyNames())
                {
                    using RegistryKey? instance = instances.OpenSubKey(subKeyName);
                    if (instance?.GetValue("gamepath") is string root && !string.IsNullOrWhiteSpace(root))
                    {
                        result.Add((subKeyName, root));
                    }
                }
            }

            using RegistryKey? active = Registry.CurrentUser.OpenSubKey(@"Software\TCGame\kart");
            if (active?.GetValue("gamepath") is string activeRoot && !string.IsNullOrWhiteSpace(activeRoot))
            {
                result.Add(("현재 gamepath", activeRoot));
            }
        }
        catch (Exception exception) when (IsDiscoveryException(exception))
        {
            warnings.Add($"레지스트리 인스턴스 목록을 읽지 못했습니다: {exception.Message}");
        }
        return result;
    }

    private static void TryAdd(
        IDictionary<string, ClientInstanceOption> instances,
        ClientInstanceCandidate candidate,
        Func<string, bool> isKnownOriginal,
        Func<string, bool> isKnownConfigurable,
        Action<string> recover,
        Func<string, string?> usernameReader)
    {
        try
        {
            string root = NormalizeDirectory(candidate.RootDirectory);
            string executable = Path.Combine(root, "KartRider.exe");
            if (!File.Exists(executable) || !isKnownOriginal(executable))
            {
                return;
            }

            recover(root);
            if (!isKnownConfigurable(executable))
            {
                return;
            }

            instances.TryAdd(
                executable,
                new ClientInstanceOption(
                    NormalizeDisplayName(candidate.Name, root),
                    root,
                    executable,
                    usernameReader(root)));
        }
        catch (Exception exception) when (IsDiscoveryException(exception))
        {
            // Discovery is best-effort. One unavailable client must not hide the others.
        }
    }

    private static PortableCatalog? TryReadCatalog(
        string baseDirectory,
        ICollection<string> warnings)
    {
        string path = Path.Combine(baseDirectory, CatalogFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            PortableCatalog catalog = ReadCatalog(path);
            if (catalog.Version != CatalogVersion)
            {
                warnings.Add(
                    $"{CatalogFileName} version {catalog.Version}은 지원하지 않아 건너뜁니다.");
                return null;
            }
            return catalog;
        }
        catch (Exception exception) when (IsCatalogReadException(exception))
        {
            warnings.Add($"{CatalogFileName}을 읽지 못해 자동 검색을 계속합니다: {exception.Message}");
            return null;
        }
    }

    private static PortableCatalog ReadCatalogForWrite(string path)
    {
        if (!File.Exists(path))
        {
            return new PortableCatalog
            {
                Version = CatalogVersion,
                Instances = new List<PortableInstanceEntry?>()
            };
        }

        PortableCatalog catalog = ReadCatalog(path);
        if (catalog.Version != CatalogVersion)
        {
            throw new InvalidDataException(
                $"지원하지 않는 {CatalogFileName} version {catalog.Version}을 덮어쓸 수 없습니다.");
        }
        return catalog;
    }

    private static PortableCatalog ReadCatalog(string path)
    {
        using FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= 0 || stream.Length > MaximumCatalogLength)
        {
            throw new InvalidDataException(
                $"{CatalogFileName} 크기가 허용 범위를 벗어났습니다.");
        }

        byte[] bytes = new byte[checked((int)stream.Length)];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException($"{CatalogFileName}을 읽는 중 파일이 잘렸습니다.");
            }
            offset += read;
        }
        PortableCatalog? catalog = JsonSerializer.Deserialize<PortableCatalog>(bytes, JsonOptions);
        if (catalog == null || catalog.Instances == null)
        {
            throw new InvalidDataException($"{CatalogFileName} 구조가 올바르지 않습니다.");
        }
        if (catalog.Instances.Count > MaximumEntries)
        {
            throw new InvalidDataException(
                $"{CatalogFileName} 항목이 {MaximumEntries}개를 초과합니다.");
        }
        return catalog;
    }

    private static FileStream AcquireCatalogLock(string lockPath)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
            }
            catch (IOException) when (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void WriteCatalogAtomically(string path, PortableCatalog catalog)
    {
        byte[] serialized = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(catalog, JsonOptions) + Environment.NewLine);
        if (serialized.Length > MaximumCatalogLength)
        {
            throw new InvalidDataException($"{CatalogFileName}이 너무 큽니다.");
        }

        string temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(serialized, 0, serialized.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? ResolveStoredPath(string baseDirectory, string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || storedPath.Length > MaximumPathLength)
        {
            return null;
        }

        try
        {
            string path = storedPath.Trim();
            if (Path.IsPathFullyQualified(path))
            {
                return NormalizeDirectory(path);
            }
            if (Path.IsPathRooted(path))
            {
                return null;
            }

            string resolved = NormalizeDirectory(Path.Combine(baseDirectory, path));
            return IsSameOrDescendant(baseDirectory, resolved) ? resolved : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string ToStoredPath(string baseDirectory, string rootDirectory)
    {
        if (!IsSameOrDescendant(baseDirectory, rootDirectory))
        {
            return rootDirectory;
        }

        string relative = Path.GetRelativePath(baseDirectory, rootDirectory);
        return relative == "."
            ? "."
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool IsSameOrDescendant(string baseDirectory, string candidate)
    {
        if (string.Equals(baseDirectory, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = baseDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? baseDirectory
            : baseDirectory + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? pathRoot = Path.GetPathRoot(fullPath);
        return !string.IsNullOrEmpty(pathRoot) &&
               string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeDisplayName(string? value, string rootDirectory)
    {
        string fallback = Path.GetFileName(
            rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fallback))
        {
            fallback = "KartRider";
        }

        string name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (name.Any(char.IsControl))
        {
            name = fallback;
        }
        return name.Length <= MaximumNameLength ? name : name[..MaximumNameLength];
    }

    private static bool IsCatalogReadException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or
        ArgumentException or NotSupportedException;

    private static bool IsDiscoveryException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or XmlException or
        JsonException or ArgumentException or NotSupportedException or OverflowException;

    private sealed record ClientInstanceCandidate(string Name, string RootDirectory);

    private sealed class PortableCatalog
    {
        public int Version { get; set; }
        public string? LastSelectedPath { get; set; }
        public List<PortableInstanceEntry?>? Instances { get; set; }
    }

    private sealed class PortableInstanceEntry
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
    }
}
