using System.Net;
using System.Net.Sockets;

namespace KartRider.P236.Server.Tests;

public sealed class ServerLifecycleConcurrencyTests
{
    [Fact]
    public async Task StopQueuedDuringStartNeverLeaksTcpOrUdpListener()
    {
        using TemporaryDirectory temporary = new();
        for (int iteration = 0; iteration < 12; iteration++)
        {
            TaskCompletionSource enteredStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using P236Server server = new(async cancellationToken =>
            {
                enteredStart.TrySetResult();
                await releaseStart.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            });
            P236ServerOptions options = TestOptions.Create(Path.Combine(temporary.Path, iteration.ToString()));

            Task start = server.StartAsync(options);
            await enteredStart.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task stop = server.StopAsync();
            Assert.False(stop.IsCompleted);

            releaseStart.TrySetResult();
            await Task.WhenAll(start, stop).WaitAsync(TimeSpan.FromSeconds(5));

            int tcpPort = server.TcpPort;
            int udpPort = server.UdpPort;
            Assert.False(server.IsRunning);
            using (TcpListener tcpProbe = new(IPAddress.Loopback, tcpPort))
            {
                tcpProbe.Start();
                tcpProbe.Stop();
            }
            using UdpClient udpProbe = new(new IPEndPoint(IPAddress.Loopback, udpPort));
        }
    }
}
