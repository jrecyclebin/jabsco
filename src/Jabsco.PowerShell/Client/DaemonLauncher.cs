using System.Diagnostics;
using System.IO.Pipes;

namespace Jabsco.PowerShell.Client;

// Starts the Jabsco daemon process if it isn't already listening on the named pipe.
public static class DaemonLauncher
{
    public static async Task EnsureRunningAsync(CancellationToken ct = default)
    {
        if (await IsPipeAvailableAsync(ct)) return;

        var moduleDir = Path.GetDirectoryName(typeof(DaemonLauncher).Assembly.Location)!;
        var exe = Path.Combine(moduleDir, OperatingSystem.IsWindows() ? "Jabsco.Daemon.exe" : "Jabsco.Daemon");

        if (!File.Exists(exe))
            throw new FileNotFoundException($"Jabsco daemon not found at {exe}");

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })?.Dispose();

        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(250, ct);
            if (await IsPipeAvailableAsync(ct)) return;
        }
        throw new TimeoutException("Daemon did not start within the timeout period");
    }

    private static async Task<bool> IsPipeAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var probe = new NamedPipeClientStream(".", DaemonClient.PipeName, PipeDirection.InOut);
            await probe.ConnectAsync(100, ct);
            return true;
        }
        catch { return false; }
    }
}
