using Jabsco.Common.Events;
using Jabsco.Core.Persistence.Profiles;
using Jabsco.Core.Rdp;
using Jabsco.Core.Sessions;
using Jabsco.Core.VmHost;

namespace Jabsco.Core.Tests;

// The four-state connection machine: Kind is a pure function of (HasScreen, HasVmHost),
// and switch's three targets (profile / vm / disconnect) are the only transitions.
public sealed class ConnectionControllerTests
{
    [Fact]
    public void StartsDisconnected()
    {
        var (c, _) = New();
        Assert.Equal(ConnectionKind.Disconnected, c.Kind);
        Assert.False(c.HasScreen);
        Assert.False(c.HasVmHost);
    }

    [Fact]
    public async Task SwitchToRdpProfile_EntersRdp_ScreenOnly()
    {
        var (c, conn) = New(Rdp("work", "10.0.0.9"));
        await c.SwitchToProfileAsync("work");

        Assert.Equal(ConnectionKind.Rdp, c.Kind);
        Assert.True(c.HasScreen);
        Assert.False(c.HasVmHost);
        Assert.Equal("10.0.0.9", conn.Last!.Host);
    }

    [Fact]
    public async Task SwitchToHostProfile_EntersVmHost_NoScreen()
    {
        var (c, conn) = New(VmHostProfile("lab", "hv-host"));
        await c.SwitchToProfileAsync("lab");

        Assert.Equal(ConnectionKind.VmHost, c.Kind);
        Assert.False(c.HasScreen);
        Assert.True(c.HasVmHost);
        Assert.Empty(conn.Created); // host management opens no RDP screen
    }

    [Fact]
    public async Task SwitchToVmProfile_EntersVm_ScreenAndHost_WithVmConnectWiring()
    {
        var vm = Guid.NewGuid();
        var (c, conn) = New(VmProfile("dc01", "hv-host", vm));
        await c.SwitchToProfileAsync("dc01");

        Assert.Equal(ConnectionKind.Vm, c.Kind);
        Assert.True(c.HasScreen);
        Assert.True(c.HasVmHost);
        Assert.Equal("hv-host", conn.Last!.Host);
        Assert.Equal(vm, conn.Last.VmId);
        Assert.Equal(TransportKind.HvSocket, conn.Last.Transport);
    }

    [Fact]
    public async Task SwitchToVm_FromVmHost_EntersVm_HostRetained()
    {
        var vm = Guid.NewGuid();
        var (c, conn) = New(VmHostProfile("lab", "hv-host"));
        await c.SwitchToProfileAsync("lab");
        await c.SwitchToVmAsync(vm);

        Assert.Equal(ConnectionKind.Vm, c.Kind);
        Assert.True(c.HasVmHost);
        Assert.Equal(vm, conn.Last!.VmId);
        Assert.Equal(TransportKind.HvSocket, conn.Last.Transport);
    }

    [Fact]
    public async Task SwitchToVm_FromDisconnected_Throws_NoHost()
    {
        var (c, _) = New();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => c.SwitchToVmAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SwitchVmToVm_KeepsHost_AndDisposesOldScreen()
    {
        var (c, conn) = New(VmHostProfile("lab", "hv-host"));
        await c.SwitchToProfileAsync("lab");
        await c.SwitchToVmAsync(Guid.NewGuid());
        var firstScreen = (FakeRdpClient)c.Screen!;
        await c.SwitchToVmAsync(Guid.NewGuid());

        Assert.Equal(ConnectionKind.Vm, c.Kind);
        Assert.True(c.HasVmHost);
        Assert.True(firstScreen.Disposed);
        Assert.Equal(2, conn.Created.Count);
    }

    [Fact]
    public async Task Disconnect_FromVm_ReturnsToVmHost_HostRetained()
    {
        var (c, _) = New(VmProfile("dc01", "hv-host", Guid.NewGuid()));
        await c.SwitchToProfileAsync("dc01");
        var screen = (FakeRdpClient)c.Screen!;
        await c.DisconnectAsync();

        Assert.Equal(ConnectionKind.VmHost, c.Kind);
        Assert.False(c.HasScreen);
        Assert.True(c.HasVmHost);
        Assert.True(screen.Disposed);
    }

    [Fact]
    public async Task Disconnect_FromRdp_ReturnsToDisconnected()
    {
        var (c, _) = New(Rdp("work", "10.0.0.9"));
        await c.SwitchToProfileAsync("work");
        await c.DisconnectAsync();

        Assert.Equal(ConnectionKind.Disconnected, c.Kind);
        Assert.False(c.HasScreen);
        Assert.False(c.HasVmHost);
    }

    [Fact]
    public async Task SwitchToRdpProfile_FromVm_DropsHost_AndDisposesOldScreen()
    {
        var (c, _) = New(VmProfile("dc01", "hv-host", Guid.NewGuid()), Rdp("plain", "10.0.0.9"));
        await c.SwitchToProfileAsync("dc01");
        var vmScreen = (FakeRdpClient)c.Screen!;
        await c.SwitchToProfileAsync("plain");

        Assert.Equal(ConnectionKind.Rdp, c.Kind);
        Assert.True(c.HasScreen);
        Assert.False(c.HasVmHost);
        Assert.True(vmScreen.Disposed);
    }

    [Fact]
    public async Task SwitchToHostProfile_FromRdp_ReAcquiresHost()
    {
        // The round-trip that profile-as-host unlocks: RDP -> host profile -> VM host.
        var (c, _) = New(Rdp("plain", "10.0.0.9"), VmHostProfile("lab", "hv-host"));
        await c.SwitchToProfileAsync("plain");
        var rdpScreen = (FakeRdpClient)c.Screen!;
        await c.SwitchToProfileAsync("lab");

        Assert.Equal(ConnectionKind.VmHost, c.Kind);
        Assert.False(c.HasScreen);
        Assert.True(c.HasVmHost);
        Assert.True(rdpScreen.Disposed);
    }

    [Fact]
    public async Task Describe_OnHost_ListsProfilesAndVms_AndReQueriesForLiveState()
    {
        var dc = new VmInfo(Guid.NewGuid(), "DC01", VmState.Running);
        var web = new VmInfo(Guid.NewGuid(), "Web01", VmState.Stopped);
        var host = new FakeVmHost(dc, web);
        var c = NewOnHost(host, VmHostProfile("lab", "hv-host"));
        await c.SwitchToProfileAsync("lab");

        var text = await c.DescribeAsync();

        Assert.Contains("DC01", text);
        Assert.Contains("Web01", text);
        Assert.Contains("lab", text);
        Assert.Contains(dc.Id.ToString(), text);
        // Describe re-queries each call so async state changes (a VM coming up) are observed.
        var before = host.ListCalls;
        await c.DescribeAsync();
        Assert.Equal(before + 1, host.ListCalls);
    }

    [Fact]
    public async Task RunVmAction_NullTarget_UsesCurrentVm_AndRefreshes()
    {
        var vm = Guid.NewGuid();
        var host = new FakeVmHost(new VmInfo(vm, "DC01", VmState.Stopped));
        var c = NewOnHost(host, VmProfile("dc01", "hv-host", vm));
        await c.SwitchToProfileAsync("dc01");

        var summary = await c.RunVmActionAsync(VmOperation.Start, null);

        Assert.Equal(vm, host.ChangedVm);
        Assert.Equal(VmOperation.Start, host.ChangedOp);
        Assert.Contains("start", summary);
    }

    [Fact]
    public async Task RunVmAction_NoHost_Throws()
    {
        var (c, _) = New();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => c.RunVmActionAsync(VmOperation.Start, Guid.NewGuid()));
    }

    [Fact]
    public async Task RunVmSetup_NoVmId_Creates_AndReturnsNewId()
    {
        var host = new FakeVmHost();
        var c = NewOnHost(host);
        await c.StartHostAsync(new HostConnection("hv-host", "admin", "pw"), vmId: null);

        var summary = await c.RunVmSetupAsync(new VmSpec(Name: "build01", Generation: VmGeneration.Gen2), vmId: null);

        Assert.Equal("build01", host.CreatedSpec!.Name);
        Assert.Contains(host.CreatedId.ToString(), summary);
    }

    [Fact]
    public async Task RunVmSetup_WithVmId_Alters()
    {
        var vm = Guid.NewGuid();
        var host = new FakeVmHost(new VmInfo(vm, "DC01", VmState.Stopped));
        var c = NewOnHost(host);
        await c.StartHostAsync(new HostConnection("hv-host", "admin", "pw"), vmId: null);

        await c.RunVmSetupAsync(new VmSpec(MemoryMB: 8192), vm);

        Assert.Equal(vm, host.AlteredVm);
        Assert.Equal(8192, host.AlteredSpec!.MemoryMB);
    }

    [Fact]
    public async Task RunVmSetup_AlterRunningVm_HardwareChange_Throws()
    {
        var vm = Guid.NewGuid();
        var host = new FakeVmHost(new VmInfo(vm, "DC01", VmState.Running));
        var c = NewOnHost(host);
        await c.StartHostAsync(new HostConnection("hv-host", "admin", "pw"), vmId: null);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => c.RunVmSetupAsync(new VmSpec(MemoryMB: 8192), vm));
        Assert.Null(host.AlteredVm);
    }

    [Fact]
    public async Task RunVmSetup_AlterRunningVm_LiveSafeChange_Ok()
    {
        var vm = Guid.NewGuid();
        var host = new FakeVmHost(new VmInfo(vm, "DC01", VmState.Running));
        var c = NewOnHost(host);
        await c.StartHostAsync(new HostConnection("hv-host", "admin", "pw"), vmId: null);

        await c.RunVmSetupAsync(new VmSpec(GuestServices: true), vm);

        Assert.Equal(vm, host.AlteredVm);
    }

    [Fact]
    public async Task RunVmSetup_Create_NoName_Throws()
    {
        var host = new FakeVmHost();
        var c = NewOnHost(host);
        await c.StartHostAsync(new HostConnection("hv-host", "admin", "pw"), vmId: null);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => c.RunVmSetupAsync(new VmSpec(Generation: VmGeneration.Gen2), vmId: null));
    }

    [Fact]
    public async Task RunVmSetup_NoHost_Throws()
    {
        var (c, _) = New();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => c.RunVmSetupAsync(new VmSpec(Name: "x"), vmId: null));
    }

    [Fact]
    public async Task StartRdp_EntersRdp_WithExplicitOptions()
    {
        var (c, conn) = New();
        await c.StartRdpAsync(new ConnectOptions(Host: "10.0.0.9", Password: "live-pw"));

        Assert.Equal(ConnectionKind.Rdp, c.Kind);
        Assert.Equal("live-pw", conn.Last!.Password);
    }

    [Fact]
    public async Task StartHost_NoVm_EntersHostView_AndCachesVms()
    {
        var host = new FakeVmHost(new VmInfo(Guid.NewGuid(), "DC01", VmState.Running));
        var c = NewOnHost(host);
        await c.StartHostAsync(new HostConnection("hv-host", "admin", "live-pw"), vmId: null);

        Assert.Equal(ConnectionKind.VmHost, c.Kind);
        Assert.Single(c.Vms);
        Assert.Equal("DC01", c.Vms[0].Name);
    }

    [Fact]
    public async Task StartHost_WithVm_RetainsLiveCreds_ForVmSwitch()
    {
        var host = new FakeVmHost();
        var c = NewOnHostConn(host, out var conn);
        await c.StartHostAsync(new HostConnection("hv-host", "admin", "live-pw"), vmId: Guid.NewGuid());

        Assert.Equal(ConnectionKind.Vm, c.Kind);
        Assert.Equal("live-pw", conn.Last!.Password);
        Assert.Equal(TransportKind.HvSocket, conn.Last.Transport);
    }

    [Fact]
    public async Task SwitchToRdpProfile_ResolvesPasswordViaLookup()
    {
        var conn = new FakeRdpConnector();
        var c = new ConnectionController(conn, new FakeProfileDirectory([Rdp("work", "10.0.0.9")]),
            _ => Task.FromResult<string?>("resolved-pw"), HostFactory());

        await c.SwitchToProfileAsync("work");

        Assert.Equal("resolved-pw", conn.Last!.Password);
    }

    [Fact]
    public async Task Dispose_DropsScreen()
    {
        var (c, _) = New(Rdp("work", "10.0.0.9"));
        await c.SwitchToProfileAsync("work");
        var screen = (FakeRdpClient)c.Screen!;
        await c.DisposeAsync();

        Assert.True(screen.Disposed);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private static (ConnectionController, FakeRdpConnector) New(params Profile[] profiles)
    {
        var conn = new FakeRdpConnector();
        var c = new ConnectionController(conn, new FakeProfileDirectory(profiles), NoPassword, HostFactory());
        return (c, conn);
    }

    private static ConnectionController NewOnHost(FakeVmHost host, params Profile[] profiles) =>
        new(new FakeRdpConnector(), new FakeProfileDirectory(profiles), NoPassword, HostFactory(host));

    private static ConnectionController NewOnHostConn(FakeVmHost host, out FakeRdpConnector conn)
    {
        conn = new FakeRdpConnector();
        return new ConnectionController(conn, new FakeProfileDirectory([]), NoPassword, HostFactory(host));
    }

    private static Func<Profile, Task<string?>> NoPassword => _ => Task.FromResult<string?>(null);

    private static Func<HostConnection, IVmHost> HostFactory(FakeVmHost? host = null) => _ => host ?? new FakeVmHost();

    private static Profile Rdp(string name, string host) => Prof(name, host, "tcp", null);
    private static Profile VmHostProfile(string name, string host) => Prof(name, host, "hvsocket", null);
    private static Profile VmProfile(string name, string host, Guid vmId) => Prof(name, host, "hvsocket", vmId);

    private static Profile Prof(string name, string host, string transport, Guid? vmId) => new(
        Id: 1, Name: name, Host: host, Port: 3389, VmId: vmId, Username: null,
        CredentialRef: null, Transport: transport, Resolution: "1280x800", LastModel: null,
        ToolPolicyId: null, CreatedAt: DateTimeOffset.UtcNow, LastUsedAt: DateTimeOffset.UtcNow,
        UseCount: 0);

    private sealed class FakeRdpConnector : IRdpConnector
    {
        public List<FakeRdpClient> Created { get; } = [];
        public ConnectOptions? Last { get; private set; }

        public Task<IRdpClient> ConnectAsync(ConnectOptions options, CancellationToken ct = default)
        {
            Last = options;
            var client = new FakeRdpClient();
            Created.Add(client);
            return Task.FromResult<IRdpClient>(client);
        }
    }

    private sealed class FakeProfileDirectory(Profile[] profiles) : IProfileDirectory
    {
        public Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Profile>>(profiles);

        public Task<Profile?> FindByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(profiles.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FakeVmHost : IVmHost
    {
        private readonly List<VmInfo> _vms;
        public FakeVmHost(params VmInfo[] vms) => _vms = [.. vms];

        public int ListCalls { get; private set; }
        public Guid? ChangedVm { get; private set; }
        public VmOperation? ChangedOp { get; private set; }
        public VmSpec? CreatedSpec { get; private set; }
        public Guid CreatedId { get; } = Guid.NewGuid();
        public Guid? AlteredVm { get; private set; }
        public VmSpec? AlteredSpec { get; private set; }

        public Task<IReadOnlyList<VmInfo>> ListVmsAsync(CancellationToken ct = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<VmInfo>>(_vms);
        }

        public Task ChangeStateAsync(Guid vmId, VmOperation operation, CancellationToken ct = default)
        {
            ChangedVm = vmId;
            ChangedOp = operation;
            return Task.CompletedTask;
        }

        public Task<Guid> CreateVmAsync(VmSpec spec, CancellationToken ct = default)
        {
            CreatedSpec = spec;
            return Task.FromResult(CreatedId);
        }

        public Task AlterVmAsync(Guid vmId, VmSpec spec, CancellationToken ct = default)
        {
            AlteredVm = vmId;
            AlteredSpec = spec;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRdpClient : IRdpClient
    {
        public bool Disposed { get; private set; }
        public ConnectionState State => ConnectionState.Connected;
        public (int Width, int Height) Resolution => (1280, 800);
        public event EventHandler<ConnectionState>? StateChanged { add { } remove { } }

        public Task ConnectAsync(ConnectOptions options, CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<byte[]> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult<byte[]>([]);
        public Task MouseMoveAsync(int x, int y, CancellationToken ct) => Task.CompletedTask;
        public Task MouseClickAsync(MouseButton button, int x, int y, CancellationToken ct) => Task.CompletedTask;
        public Task MouseScrollAsync(int x, int y, ScrollDirection direction, int amount, CancellationToken ct) => Task.CompletedTask;
        public Task KeyPressAsync(string keys, CancellationToken ct) => Task.CompletedTask;
        public Task TypeTextAsync(string text, CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
