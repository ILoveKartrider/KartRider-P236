using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KartRider.P236.ItemProbabilities;

/// <summary>Loads and atomically saves the public item-probability JSON v1 format.</summary>
public static class ItemProbabilityConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ItemProbabilityConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);

        try
        {
            byte[] json = File.ReadAllBytes(fullPath);
            ItemProbabilityConfiguration configuration =
                JsonSerializer.Deserialize<ItemProbabilityConfiguration>(json, SerializerOptions)
                ?? throw new ItemProbabilityConfigurationException("The item probability JSON root is null.");
            configuration.Validate();
            return configuration;
        }
        catch (JsonException exception)
        {
            throw new ItemProbabilityConfigurationException(
                $"The item probability JSON file is malformed: {fullPath}", exception);
        }
    }

    public static void Save(string path, ItemProbabilityConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The configuration path has no parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(configuration, SerializerOptions);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.WriteByte((byte)'\n');
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
}
