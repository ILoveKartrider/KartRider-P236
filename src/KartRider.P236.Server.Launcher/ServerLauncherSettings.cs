using System.Net;
using System.Net.Sockets;

namespace KartRider.P236.Server.Launcher;

internal sealed class ServerLauncherSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string BindAddress { get; set; } = "127.0.0.1";

    public string AdvertisedAddress { get; set; } = "127.0.0.1";

    public int TcpPort { get; set; } = 39312;

    public int UdpPort { get; set; } = 39312;

    public string ServerDataDirectory { get; set; } = DefaultDataDirectory;

    public string LogDirectory { get; set; } = DefaultLogDirectory;

    public bool EnablePacketTrace { get; set; }

    public string ClientDataDirectory { get; set; } = string.Empty;

    public string ItemProbabilityConfigurationPath { get; set; } = DefaultItemConfigurationPath;

    public static string DefaultDataDirectory =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));

    public static string DefaultLogDirectory =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "logs"));

    public static string DefaultItemConfigurationPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "item-probabilities.json"));

    public void Validate()
    {
        if (Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"지원하지 않는 서버 런처 설정 버전입니다: {Version} (expected {CurrentVersion}).");
        }

        ValidateIpv4(BindAddress, "바인드 IPv4");
        ValidateIpv4(AdvertisedAddress, "광고 IPv4");
        ValidatePort(TcpPort, "TCP 포트");
        ValidatePort(UdpPort, "UDP 포트");
        ValidatePath(ServerDataDirectory, "서버 데이터 폴더", allowEmpty: false);
        ValidatePath(LogDirectory, "로그 폴더", allowEmpty: false);
        ValidatePath(ClientDataDirectory, "클라이언트 Data 폴더", allowEmpty: true);
        ValidatePath(ItemProbabilityConfigurationPath, "아이템 확률 JSON", allowEmpty: false);
    }

    private static void ValidateIpv4(string value, string label)
    {
        if (!IPAddress.TryParse(value, out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException($"{label} 값은 IPv4 주소여야 합니다.");
        }
    }

    private static void ValidatePort(int value, string label)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException($"{label} 값은 0~65535 범위여야 합니다.");
        }
    }

    private static void ValidatePath(string value, string label, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (allowEmpty)
            {
                return;
            }

            throw new InvalidDataException($"{label} 값이 비어 있습니다.");
        }

        if (value.Length > 1024 || value.Any(char.IsControl))
        {
            throw new InvalidDataException($"{label} 값이 올바르지 않습니다.");
        }

        try
        {
            _ = Path.GetFullPath(value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException($"{label} 경로가 올바르지 않습니다.", exception);
        }
    }
}
