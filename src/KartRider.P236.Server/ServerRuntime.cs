namespace KartRider.P236.Server;

internal static class ServerRuntime
{
    private static P236ServerOptions _options = new();
    public static P236ServerOptions Options => Volatile.Read(ref _options);
    public static void Configure(P236ServerOptions options) => Volatile.Write(ref _options, options);
}
