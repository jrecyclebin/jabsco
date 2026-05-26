using System.Runtime.InteropServices;
using Jabsco.Common.Events;
using Jabsco.Core.Rdp.Input;
using Jabsco.Core.Rdp.Interop;
using Jabsco.Core.Rdp.Screenshot;
using Microsoft.Extensions.Logging;

namespace Jabsco.Core.Rdp;

public sealed class FreeRdpClient : IRdpClient
{
    private readonly ILogger<FreeRdpClient> _logger;
    private IntPtr _instance;
    private IntPtr _context;    // rdpContext* — valid after ContextNew
    private IntPtr _gdi;        // rdpGdi*     — valid after GdiInit
    private InputDriver? _input;
    private readonly Framebuffer _framebuffer = new();
    private ConnectionState _state = ConnectionState.Disconnected;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public ConnectionState State => _state;
    public (int Width, int Height) Resolution { get; private set; } = (1280, 800);
    public event EventHandler<ConnectionState>? StateChanged;

    public FreeRdpClient(ILogger<FreeRdpClient> logger) => _logger = logger;

    public async Task ConnectAsync(ConnectOptions options, CancellationToken ct)
    {
        SetState(ConnectionState.Connecting);
        try
        {
            _instance = FreeRdpNative.New();
            if (_instance == IntPtr.Zero)
                throw new InvalidOperationException("freerdp_new returned null");

            if (!FreeRdpNative.ContextNew(_instance))
                throw new InvalidOperationException("freerdp_context_new failed");

            // freerdp.context is at offset 0 — store it so we don't re-read every time
            _context = Marshal.ReadIntPtr(_instance, FreeRdpNative.ContextOffset);
            if (_context == IntPtr.Zero)
                throw new InvalidOperationException("rdpContext pointer is null after context_new");

            ApplySettings(options);

            await Task.Run(() =>
            {
                if (!FreeRdpNative.Connect(_instance))
                    throw new InvalidOperationException(
                        $"freerdp_connect failed: {FreeRdpNative.DescribeLastError(_context)}");
            }, ct);

            if (!FreeRdpNative.GdiInit(_instance, FreeRdpNative.PixelFormatBgra32))
                throw new InvalidOperationException("gdi_init failed");

            // rdpContext.gdi is populated by gdi_init
            _gdi = Marshal.ReadIntPtr(_context, FreeRdpNative.GdiOffset);
            if (_gdi == IntPtr.Zero)
                throw new InvalidOperationException("rdpGdi pointer is null after gdi_init");

            Resolution = (options.Width, options.Height);

            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopTask = Task.Run(() => RunMessageLoop(_loopCts.Token), CancellationToken.None);

            SetState(ConnectionState.Connected);
        }
        catch
        {
            SetState(ConnectionState.Failed);
            Cleanup();
            throw;
        }
    }

    private void ApplySettings(ConnectOptions options)
    {
        // On Windows the struct layout of rdpContext differs from the Linux build,
        // so we use a C wrapper in our shim DLL. On Linux the offset is correct.
        var settings = OperatingSystem.IsWindows()
            ? FreeRdpNative.GetSettings(_instance)
            : Marshal.ReadIntPtr(_context, FreeRdpNative.SettingsOffset);
        if (settings == IntPtr.Zero)
            throw new InvalidOperationException("rdpSettings pointer is null");

        if (!FreeRdpNative.SettingsSetString(settings, FreeRdpNative.ServerHostname, options.Host))
            throw new InvalidOperationException($"freerdp_settings_set_string failed for hostname");
        FreeRdpNative.SettingsSetUInt16(settings, FreeRdpNative.ServerPort, (ushort)options.Port);

        if (options.Username != null)
            FreeRdpNative.SettingsSetString(settings, FreeRdpNative.Username, options.Username);
        if (options.Password != null)
            FreeRdpNative.SettingsSetString(settings, FreeRdpNative.Password, options.Password);
        if (options.Domain != null)
            FreeRdpNative.SettingsSetString(settings, FreeRdpNative.Domain, options.Domain);

        FreeRdpNative.SettingsSetUInt32(settings, FreeRdpNative.DesktopWidth, (uint)options.Width);
        FreeRdpNative.SettingsSetUInt32(settings, FreeRdpNative.DesktopHeight, (uint)options.Height);
        FreeRdpNative.SettingsSetUInt32(settings, FreeRdpNative.ColorDepth, 32);

        if (options.AcceptAnyCertificate)
        {
            FreeRdpNative.SettingsSetBool(settings, FreeRdpNative.AutoAcceptCertificate, true);
            FreeRdpNative.SettingsSetBool(settings, FreeRdpNative.IgnoreCertificate, true);
        }

        if (options.VmId.HasValue)
        {
            // Hyper-V VMConnect: connect to host:2179 with VM GUID preconnection PDU.
            // The Hyper-V host proxies this to the VM's console (basic or enhanced session).
            FreeRdpNative.SettingsSetUInt32(settings, FreeRdpNative.ServerPort, 2179);
            FreeRdpNative.SettingsSetBool(settings, FreeRdpNative.VmConnectMode, true);
            FreeRdpNative.SettingsSetBool(settings, FreeRdpNative.NegotiateSecurityLayer, false);
            FreeRdpNative.SettingsSetBool(settings, FreeRdpNative.SendPreconnectionPdu, true);
            FreeRdpNative.SettingsSetString(settings, FreeRdpNative.PreconnectionBlob,
                options.VmId.Value.ToString("D").ToUpperInvariant());
        }
    }

    private void RunMessageLoop(CancellationToken ct)
    {
        _logger.LogDebug("FreeRDP message loop started");
        while (!ct.IsCancellationRequested && !FreeRdpNative.ShallDisconnectContext(_context))
        {
            try
            {
                FreeRdpNative.CheckEventHandles(_context);
                CaptureFramebuffer();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in FreeRDP message loop");
            }
            Thread.Sleep(16); // ~60 fps
        }
        _logger.LogDebug("FreeRDP message loop exited");
    }

    private void CaptureFramebuffer()
    {
        if (_gdi == IntPtr.Zero) return;

        int width  = Marshal.ReadInt32(_gdi, FreeRdpNative.GdiWidthOffset);
        int height = Marshal.ReadInt32(_gdi, FreeRdpNative.GdiHeightOffset);

        if (width <= 0 || height <= 0) return;

        IntPtr buffer = Marshal.ReadIntPtr(_gdi, FreeRdpNative.GdiPrimaryBufferOffset);
        if (buffer == IntPtr.Zero) return;

        _framebuffer.Update(buffer, width, height);
    }

    public async Task<byte[]> CaptureScreenshotPngAsync(CancellationToken ct)
    {
        for (int i = 0; i < 5; i++)
        {
            var png = _framebuffer.TryCopyPng();
            if (png != null) return png;
            await Task.Delay(100, ct);
        }
        throw new InvalidOperationException("Framebuffer not ready after 500ms — connection may not have completed");
    }

    private IntPtr GetInputPtr()
    {
        // rdpContext.input is at offset 304 (verified via offsetof)
        var input = Marshal.ReadIntPtr(_context, FreeRdpNative.InputOffset);
        if (input == IntPtr.Zero)
            throw new InvalidOperationException("rdpInput pointer is null — is the session connected?");
        return input;
    }

    private InputDriver RequireInput() => _input ??= new InputDriver(GetInputPtr());

    public Task MouseMoveAsync(int x, int y, CancellationToken ct)
    {
        RequireInput().MouseMove(x, y);
        return Task.CompletedTask;
    }

    public Task MouseClickAsync(MouseButton button, int x, int y, CancellationToken ct)
    {
        var drv = RequireInput();
        drv.MouseClick(button, x, y, down: true);
        drv.MouseClick(button, x, y, down: false);
        return Task.CompletedTask;
    }

    public Task MouseScrollAsync(int x, int y, ScrollDirection direction, int amount, CancellationToken ct)
    {
        RequireInput().MouseScroll(x, y, direction, amount);
        return Task.CompletedTask;
    }

    public Task KeyPressAsync(string keys, CancellationToken ct)
    {
        RequireInput().KeyChord(keys);
        return Task.CompletedTask;
    }

    public async Task TypeTextAsync(string text, CancellationToken ct)
    {
        var driver = RequireInput();
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (char c in text)
        {
            driver.TypeChar(c);
            // Pace input: flush each char before the next is queued.
            // Sending all events in a burst causes misprocessing on the server side.
            await Task.Delay(30, ct);
        }
    }

    private void SetState(ConnectionState s)
    {
        _state = s;
        StateChanged?.Invoke(this, s);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        _loopCts?.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(2), ct); }
            catch (TimeoutException) { _logger.LogWarning("FreeRDP message loop did not exit within 2s"); }
            catch (OperationCanceledException) { }
        }
        Cleanup();
        SetState(ConnectionState.Disconnected);
    }

    private void Cleanup()
    {
        if (_instance != IntPtr.Zero)
        {
            try { FreeRdpNative.GdiFree(_instance); } catch { }
            try { FreeRdpNative.Disconnect(_instance); } catch { }
            try { FreeRdpNative.ContextFree(_instance); } catch { }
            try { FreeRdpNative.Free(_instance); } catch { }
            _instance = IntPtr.Zero;
            _context = IntPtr.Zero;
            _gdi = IntPtr.Zero;
        }
        _framebuffer.Dispose();
        _input = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_state != ConnectionState.Disconnected)
            await DisconnectAsync(CancellationToken.None);
    }
}
