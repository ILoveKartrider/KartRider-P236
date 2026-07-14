using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using KartRider.Common.Utilities;

namespace KartRider.P236.Server.Tests;

public sealed class ServerLifecycleTests
{
    [Fact]
    public void DefaultsToLoopbackOnly()
    {
        P236ServerOptions options = new();
        Assert.Equal(IPAddress.Loopback, options.BindAddress);
        Assert.Equal(IPAddress.Loopback, options.AdvertisedAddress);
    }

    [Fact]
    public void RejectsIpv6Endpoints()
    {
        P236ServerOptions options = new() { AdvertisedAddress = IPAddress.IPv6Loopback };
        ArgumentException exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("IPv4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartsTcpAndUdpCreatesDataAndStopsCleanly()
    {
        using TemporaryDirectory temporary = new();
        P236ServerOptions options = TestOptions.Create(temporary.Path);
        await using P236Server server = new();
        await server.StartAsync(options);
        int tcpPort = server.TcpPort;
        int udpPort = server.UdpPort;
        Assert.True(tcpPort > 0);
        Assert.True(udpPort > 0);

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, tcpPort);
        NetworkStream stream = client.GetStream();
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header);
        int payloadLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header));
        byte[] payload = new byte[payloadLength];
        await stream.ReadExactlyAsync(payload);
        Assert.Equal(Adler32Helper.GenerateAdler32_ASCII("PcFirstMessage"), BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.True(File.Exists(Path.Combine(options.DataDirectory, "profiles.json")));
        Assert.True(File.Exists(Path.Combine(options.DataDirectory, "observers.json")));
        Assert.False(File.Exists(Path.Combine(options.LogDirectory, "p236-packets.log")));

        await server.StopAsync();
        Assert.False(server.IsRunning);
        using TcpListener tcpProbe = new(IPAddress.Loopback, tcpPort);
        tcpProbe.Start();
        tcpProbe.Stop();
        using UdpClient udpProbe = new(new IPEndPoint(IPAddress.Loopback, udpPort));
    }
}
