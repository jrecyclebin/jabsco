using Jabsco.Common.Events;
using Jabsco.Core.Persistence.Profiles;

namespace Jabsco.Core.Rdp;

public interface IRdpClient : IAsyncDisposable
{
    Task ConnectAsync(ConnectOptions options, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    ConnectionState State { get; }
    (int Width, int Height) Resolution { get; }

    Task<byte[]> CaptureScreenshotPngAsync(CancellationToken ct);

    Task MouseMoveAsync(int x, int y, CancellationToken ct);
    Task MouseClickAsync(MouseButton button, int x, int y, CancellationToken ct);
    Task MouseScrollAsync(int x, int y, ScrollDirection direction, int amount, CancellationToken ct);
    Task KeyPressAsync(string keys, CancellationToken ct);
    Task TypeTextAsync(string text, CancellationToken ct);

    event EventHandler<ConnectionState>? StateChanged;
}

public sealed record ConnectOptions(
    string Host,
    int Port = 3389,
    string? Username = null,
    string? Password = null,
    string? Domain = null,
    int Width = 1280,
    int Height = 800,
    TransportKind Transport = TransportKind.Tcp,
    Guid? VmId = null,
    bool AcceptAnyCertificate = false)
{
    public static ConnectOptions FromProfile(Profile p)
    {
        var (width, height) = ParseResolution(p.Resolution);
        return new ConnectOptions(
            Host: p.Host,
            Port: p.Port,
            Username: p.Username,
            Width: width,
            Height: height);
    }

    private static (int width, int height) ParseResolution(string resolution)
    {
        // Expected format: "1280x800"
        var parts = resolution.Split('x');
        if (parts.Length == 2
            && int.TryParse(parts[0], out int w)
            && int.TryParse(parts[1], out int h))
            return (w, h);
        return (1280, 800);
    }
}

public enum TransportKind { Tcp, HvSocket, Vsock }
