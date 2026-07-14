using System.Net;

namespace KartRider.P236.Server.Tests;

internal static class TestOptions
{
    public static P236ServerOptions Create(string root) => new()
    {
        BindAddress = IPAddress.Loopback,
        AdvertisedAddress = IPAddress.Loopback,
        TcpPort = 0,
        UdpPort = 0,
        DataDirectory = Path.Combine(root, "data"),
        LogDirectory = Path.Combine(root, "logs"),
        EnablePacketTrace = false
    };
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"p236-{Guid.NewGuid():N}");
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
