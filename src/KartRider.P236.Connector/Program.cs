using System.Security.Principal;

namespace KartRider.P236.Connector;

internal static class Program
{
    private const string SingleInstanceMutex = @"Local\KartRider2005Launcher.UI";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return RunCommandLine(args);
        }

        using Mutex instanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutex,
            out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "KartRider 2005 접속기가 이미 실행 중입니다.",
                "KartRider 2005 접속기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
        return 0;
    }

    private static int RunCommandLine(string[] args)
    {
        if (args.Length != 5 || !string.Equals(args[0], "--configure", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Usage: KartRider.P236.Connector --configure <client-root> <server-ipv4> <login-port> <storage-root>");
            return 2;
        }

        try
        {
            string userSid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query))
            {
                userSid = identity.User?.Value
                    ?? throw new InvalidOperationException("현재 Windows 사용자 SID를 확인할 수 없습니다.");
            }

            using Mutex launchGate = new Mutex(
                initiallyOwned: false,
                name: $@"Local\KartRider2005Launcher.LaunchGate.{userSid}");
            bool ownsLaunchGate = false;
            try
            {
                try
                {
                    ownsLaunchGate = launchGate.WaitOne(TimeSpan.FromSeconds(30));
                }
                catch (AbandonedMutexException)
                {
                    ownsLaunchGate = true;
                }
                if (!ownsLaunchGate)
                {
                    throw new TimeoutException("다른 KartRider 2005 접속기가 실행 또는 설정 저장 중입니다.");
                }

                ClientSelection selection = ClientSelection.FromPath(args[1]);
                _ = OriginalClientValidator.ValidateConfigurableClient(selection);
                if (!ushort.TryParse(args[3], out ushort loginPort) || loginPort == 0)
                {
                    throw new InvalidOperationException("로그인 TCP 포트는 1~65535 범위여야 합니다.");
                }

                PreparedClientSettingsResult result = PreparedClientSettings.Apply(
                    selection.RootDirectory,
                    args[2],
                    loginPort,
                    args[4]);
                Console.WriteLine(
                    $"changed={result.Changed} endpoint={result.PinInfo.LoginEndpoint} " +
                    $"storage={result.PinInfo.StorageRoot}");
                try
                {
                    string catalogPath = ClientInstanceDiscovery.RememberInstance(selection.RootDirectory);
                    Console.WriteLine($"catalog={catalogPath}");
                }
                catch (Exception exception)
                {
                    // Portable catalog persistence is best-effort and must not
                    // turn a successful PIN/XML update into a CLI failure.
                    Console.Error.WriteLine($"warning: 인스턴스 목록을 저장하지 못했습니다: {exception.Message}");
                }
                return 0;
            }
            finally
            {
                if (ownsLaunchGate)
                {
                    launchGate.ReleaseMutex();
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
