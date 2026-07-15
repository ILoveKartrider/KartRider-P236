using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KartRider.P236.Server.Launcher;

internal static class ServerLauncherSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string SettingsPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "server-launcher.json"));

    public static ServerLauncherSettings Load()
    {
        byte[] json = File.ReadAllBytes(SettingsPath);
        try
        {
            ServerLauncherSettings settings =
                JsonSerializer.Deserialize<ServerLauncherSettings>(json, SerializerOptions)
                ?? throw new InvalidDataException("서버 런처 설정 JSON 루트가 null입니다.");
            settings.Validate();
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"서버 런처 설정 JSON이 올바르지 않습니다: {SettingsPath}", exception);
        }
    }

    public static void Save(ServerLauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        string fullPath = SettingsPath;
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("서버 런처 설정 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);
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
