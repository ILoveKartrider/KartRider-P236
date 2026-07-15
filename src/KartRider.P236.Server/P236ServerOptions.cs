using System.Net;
using System.Net.Sockets;

namespace KartRider.P236.Server;

public sealed class P236ServerOptions
{
    public IPAddress BindAddress { get; set; } = IPAddress.Loopback;
    public IPAddress AdvertisedAddress { get; set; } = IPAddress.Loopback;
    public int TcpPort { get; set; } = 39312;
    public int UdpPort { get; set; } = 39312;
    public string DataDirectory { get; set; } = Path.Combine(Environment.CurrentDirectory, "data");
    public string LogDirectory { get; set; } = Path.Combine(Environment.CurrentDirectory, "logs");
    public bool EnablePacketTrace { get; set; }
    public uint FirstUserNumber { get; set; } = 201_810_291;
    public string DefaultUserId { get; set; } = "rider";
    public string DefaultNickname { get; set; } = "Rider";
    public int DefaultRp { get; set; } = 10_000_000;
    public uint DefaultLucci { get; set; } = 1_000_000;
    public uint DefaultPMap { get; set; }
    public short DefaultSlotChanger { get; set; } = 30_000;
    public byte DefaultLicenseLevel { get; set; } = 3;
    public ushort[] DefaultLicenseCompletionMasks { get; set; } = [31, 7, 31, 63, 0, 0];
    public ushort DefaultCharacter { get; set; } = 3;
    public ushort DefaultPaint { get; set; } = 1;
    public ushort DefaultKart { get; set; }
    public ushort DefaultPlate { get; set; }
    public ushort DefaultGoggle { get; set; }
    public ushort DefaultBalloon { get; set; }
    public ushort DefaultHeadBand { get; set; }
    public IReadOnlyCollection<string> DefaultObserverUsernames { get; set; } = ["observer"];

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(BindAddress);
        ArgumentNullException.ThrowIfNull(AdvertisedAddress);
        if (BindAddress.AddressFamily != AddressFamily.InterNetwork ||
            AdvertisedAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("P236 bind and advertised addresses must both be IPv4 addresses.");
        if (TcpPort is < 0 or > 65_535 || UdpPort is < 0 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(TcpPort), "Ports must be between 0 and 65535.");
        if (string.IsNullOrWhiteSpace(DataDirectory) || string.IsNullOrWhiteSpace(LogDirectory))
            throw new ArgumentException("Data and log directories are required.");
        if (FirstUserNumber == 0)
            throw new ArgumentOutOfRangeException(nameof(FirstUserNumber));
        if (DefaultLicenseLevel > P236LicenseProgress.MaximumLevel)
            throw new ArgumentOutOfRangeException(
                nameof(DefaultLicenseLevel),
                $"P236 license levels must be between 0 and {P236LicenseProgress.MaximumLevel} (L1).");
        if (DefaultLicenseCompletionMasks is null ||
            DefaultLicenseCompletionMasks.Length != P236LicenseProgress.CompletionMaskCount)
            throw new ArgumentException(
                $"Exactly {P236LicenseProgress.CompletionMaskCount} license completion masks are required.");
    }
}
