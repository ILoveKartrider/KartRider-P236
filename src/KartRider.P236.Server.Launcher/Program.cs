namespace KartRider.P236.Server.Launcher;

internal static class Program
{
    private const string SingleInstanceMutex = @"Local\KartRiderP236.ServerLauncher.UI";

    [STAThread]
    private static int Main()
    {
        using Mutex instanceMutex = new(
            initiallyOwned: true,
            SingleInstanceMutex,
            out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "KartRider P236 서버 런처가 이미 실행 중입니다.",
                "KartRider P236 서버 런처",
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
}
