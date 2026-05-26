using Jabsco.Core.Rdp;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
var logger = loggerFactory.CreateLogger<FreeRdpClient>();

var host    = args.Length > 0 ? args[0] : "DESKTOP-AEJ51QF";
var user    = args.Length > 1 ? args[1] : "Test";
var pass    = args.Length > 2 ? args[2] : "pass@word1";
var outDir  = args.Length > 3 ? args[3] : ".";

Console.WriteLine($"Connecting to {host} as {user}...");

var client = new FreeRdpClient(logger);
var options = new ConnectOptions(
    Host: host, Port: 3389,
    Username: user, Password: pass,
    Width: 1280, Height: 800,
    AcceptAnyCertificate: true);

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    await client.ConnectAsync(options, cts.Token);
    Console.WriteLine("Connected. Capturing screenshots over 3 seconds...");

    // Take a screenshot every second for 3 seconds to see when content arrives
    for (int i = 1; i <= 3; i++)
    {
        await Task.Delay(1000, cts.Token);
        var png = await client.CaptureScreenshotPngAsync(cts.Token);
        var path = Path.Combine(outDir, $"screenshot_{i}.png");
        await File.WriteAllBytesAsync(path, png);
        Console.WriteLine($"  t={i}s → {path} ({png.Length:N0} bytes)");
    }

    await client.DisconnectAsync(CancellationToken.None);
    Console.WriteLine("Done.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    return 1;
}
finally
{
    await client.DisposeAsync();
}
