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

internal static class ClientInstanceDiscovery
{
    internal static IReadOnlyList<ClientInstanceOption> FindPreparedInstances()
    {
        Dictionary<string, ClientInstanceOption> instances =
            new Dictionary<string, ClientInstanceOption>(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string root) in ReadRegistryInstances())
        {
            TryAdd(instances, name, root);
        }

        return instances.Values
            .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(instance => instance.RootDirectory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Name, string Root)> ReadRegistryInstances()
    {
        using RegistryKey? instances = Registry.CurrentUser.OpenSubKey(@"Software\TCGame\kart\instances");
        if (instances != null)
        {
            foreach (string subKeyName in instances.GetSubKeyNames())
            {
                using RegistryKey? instance = instances.OpenSubKey(subKeyName);
                if (instance?.GetValue("gamepath") is string root && !string.IsNullOrWhiteSpace(root))
                {
                    yield return (subKeyName, root);
                }
            }
        }

        // Keep the active path as a fallback, after named instance metadata so
        // duplicate paths retain their useful instance names.
        using RegistryKey? active = Registry.CurrentUser.OpenSubKey(@"Software\TCGame\kart");
        if (active?.GetValue("gamepath") is string activeRoot && !string.IsNullOrWhiteSpace(activeRoot))
        {
            yield return ("현재 gamepath", activeRoot);
        }
    }

    private static void TryAdd(
        IDictionary<string, ClientInstanceOption> instances,
        string name,
        string rootDirectory)
    {
        try
        {
            string root = Path.GetFullPath(rootDirectory);
            string executable = Path.Combine(root, "KartRider.exe");
            PreparedClientSettings.RecoverIfNeeded(root);
            if (!File.Exists(executable) || !OriginalClientValidator.IsKnownConfigurable(executable))
            {
                return;
            }

            instances.TryAdd(
                executable,
                new ClientInstanceOption(
                    string.IsNullOrWhiteSpace(name) ? Path.GetFileName(root) : name,
                    root,
                    executable,
                    LegacyAccountProfile.TryReadUsername(root)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException)
        {
            // Discovery is best-effort. The browse buttons remain available.
        }
    }

}
