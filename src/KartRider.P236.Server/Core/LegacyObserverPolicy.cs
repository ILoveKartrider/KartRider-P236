using System.Text.Json;
using KartRider.P236.Server;

namespace KartRider;

internal sealed class LegacyObserverPolicyDocument
{
    public int Version { get; set; } = 1;
    public List<string> Usernames { get; set; } = [];
}

internal static class LegacyObserverPolicy
{
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static HashSet<string> _usernames = CreateDefaultUsernames();
    private static string PolicyPath => Path.Combine(ServerRuntime.Options.DataDirectory, "observers.json");

    public static void Reload()
    {
        HashSet<string> usernames = CreateDefaultUsernames();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PolicyPath)!);
            LegacyObserverPolicyDocument document;
            if (File.Exists(PolicyPath))
            {
                using FileStream stream = File.OpenRead(PolicyPath);
                document = JsonSerializer.Deserialize<LegacyObserverPolicyDocument>(stream, SerializerOptions)
                    ?? new LegacyObserverPolicyDocument();
            }
            else
            {
                document = new LegacyObserverPolicyDocument
                {
                    Usernames = [.. ServerRuntime.Options.DefaultObserverUsernames]
                };
                using FileStream stream = new(PolicyPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(stream, document, SerializerOptions);
            }

            usernames.Clear();
            foreach (string username in document.Usernames ?? [])
                if (!string.IsNullOrWhiteSpace(username)) usernames.Add(username.Trim());
        }
        catch (Exception exception)
        {
            LegacyPacketTrace.LogEvent(
                $"[P236 OBSERVER] Unable to load '{PolicyPath}': {exception.Message}; using defaults.");
        }

        lock (SyncRoot) _usernames = usernames;
        LegacyPacketTrace.LogEvent(
            $"[P236 OBSERVER] Loaded {usernames.Count} observer username(s) from '{PolicyPath}'.");
    }

    public static bool IsObserver(LegacySessionProfile? profile)
    {
        if (profile is null) return false;
        string username = string.IsNullOrWhiteSpace(profile.SourceUsername)
            ? profile.UserId
            : profile.SourceUsername;
        lock (SyncRoot) return !string.IsNullOrWhiteSpace(username) && _usernames.Contains(username.Trim());
    }

    private static HashSet<string> CreateDefaultUsernames() =>
        new(ServerRuntime.Options.DefaultObserverUsernames, StringComparer.OrdinalIgnoreCase);
}
