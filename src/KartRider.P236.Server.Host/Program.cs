using System.Net;
using KartRider.P236.Server;

return await P236ServerHost.RunAsync(args);

internal static class P236ServerHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(value => value is "-h" or "--help"))
        {
            PrintHelp();
            return 0;
        }

        P236ServerOptions options;
        try { options = Parse(args); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            Console.Error.WriteLine($"Configuration error: {exception.Message}");
            return 2;
        }

        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        await using P236Server server = new();
        try
        {
            await server.StartAsync(options, shutdown.Token);
            Console.WriteLine($"KartRider P236 server is ready (TCP {server.TcpPort}, UDP {server.UdpPort}).");
            Console.WriteLine("Press Ctrl+C to stop.");
            await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally { await server.StopAsync(); }
        return 0;
    }

    private static P236ServerOptions Parse(string[] args)
    {
        P236ServerOptions options = new();
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            string Next()
            {
                if (++index >= args.Length) throw new ArgumentException($"{argument} requires a value.");
                return args[index];
            }
            switch (argument)
            {
                case "--bind": options.BindAddress = IPAddress.Parse(Next()); break;
                case "--advertise": options.AdvertisedAddress = IPAddress.Parse(Next()); break;
                case "--tcp-port": options.TcpPort = int.Parse(Next()); break;
                case "--udp-port": options.UdpPort = int.Parse(Next()); break;
                case "--data": options.DataDirectory = Path.GetFullPath(Next()); break;
                case "--logs": options.LogDirectory = Path.GetFullPath(Next()); break;
                case "--trace": options.EnablePacketTrace = true; break;
                default: throw new ArgumentException($"Unknown option '{argument}'.");
            }
        }
        options.Validate();
        return options;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("KartRider.P236.Server.Host");
        Console.WriteLine("  --bind IP          listen address (default 127.0.0.1)");
        Console.WriteLine("  --advertise IP     address sent to clients (default 127.0.0.1)");
        Console.WriteLine("  --tcp-port PORT    TCP port (default 39312)");
        Console.WriteLine("  --udp-port PORT    UDP port (default 39312)");
        Console.WriteLine("  --data PATH        profiles.json and observers.json directory");
        Console.WriteLine("  --logs PATH        opt-in packet trace directory");
        Console.WriteLine("  --trace             enable full packet hex trace");
    }
}
