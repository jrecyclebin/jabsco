using Avalonia;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Jabsco.Ui;

sealed class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            // WinExe doesn't allocate a console — attach to the parent's (cmd/PowerShell).
            if (OperatingSystem.IsWindows())
                AttachConsole(-1);

            return await Jabsco.Cli.CliEntry.RunAsync(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool AttachConsole(int dwProcessId);
}
