using KartRider;

namespace KartRider.P236.Server;

public sealed class P236Server : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Func<CancellationToken, Task> _beforeRouterStart;
    private int _started;
    public const ushort ProtocolVersion = 236;

    public P236Server() : this(static _ => Task.CompletedTask) { }

    internal P236Server(Func<CancellationToken, Task> beforeRouterStart) =>
        _beforeRouterStart = beforeRouterStart ?? throw new ArgumentNullException(nameof(beforeRouterStart));

    public bool IsRunning => Volatile.Read(ref _started) != 0 && RouterListener.IsRunning;
    public int TcpPort => RouterListener.TcpPort;
    public int UdpPort => RouterListener.UdpPort;

    public async Task StartAsync(P236ServerOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new P236ServerOptions();
        options.Validate();
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _started) != 0)
                throw new InvalidOperationException("The P236 server is already running.");
            await _beforeRouterStart(cancellationToken).ConfigureAwait(false);
            ServerRuntime.Configure(options);
            RouterListener.Start(options);
            Volatile.Write(ref _started, 1);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _started) == 0) return;
            RouterListener.Stop();
            Volatile.Write(ref _started, 0);
        }
        finally { _lifecycle.Release(); }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
