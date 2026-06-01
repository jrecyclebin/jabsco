using Jabsco.Common.Events;
using Jabsco.Core.Agent;
using Jabsco.Core.Approval;
using Jabsco.Core.Persistence.Policies;
using Jabsco.Core.Providers;
using Jabsco.Core.Rdp;
using SkiaSharp;

namespace Jabsco.Core.Tests;

// The loop must run with no screen: skip capture/ScreenshotEvent, feed a text observation,
// and refuse computer actions with a connect-first error rather than touching a null screen.
public sealed class AgentLoopTests
{
    [Fact]
    public async Task NullScreen_SkipsCapture_FeedsObservation()
    {
        var provider = new FakeProvider(Done("ok"));
        var conn = new FakeConnection(screen: null, observation: "VMs: dc01 (running)");
        var loop = new AgentLoop(conn, provider, new AllowApproval());

        var events = await Collect(loop);

        Assert.DoesNotContain(events, e => e is ScreenshotEvent);
        Assert.Equal(1, conn.DescribeCalls);
        Assert.Equal("VMs: dc01 (running)", provider.LastRequest!.Observation);
        Assert.Null(provider.LastRequest.PromptScreenshotPng);
    }

    [Fact]
    public async Task NullScreen_ComputerAction_ReturnsConnectError_DoesNotExecute()
    {
        var provider = new FakeProvider(Click(), Done("done"));
        var loop = new AgentLoop(new FakeConnection(null, "no screen"), provider, new AllowApproval());

        var events = await Collect(loop);

        var result = events.OfType<ToolResultEvent>().First();
        Assert.Contains("switch", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events, e => e is FinalEvent);
    }

    [Fact]
    public async Task NullScreen_NonScreenAction_StillRuns()
    {
        var provider = new FakeProvider(new WaitAction(0), Done("done"));
        var loop = new AgentLoop(new FakeConnection(null, "no screen"), provider, new AllowApproval());

        var result = (await Collect(loop)).OfType<ToolResultEvent>().First();

        Assert.Contains("wait", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("switch", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithScreen_CapturesAndEmitsScreenshot()
    {
        var screen = new RecordingScreen();
        var provider = new FakeProvider(Done("ok"));
        var loop = new AgentLoop(new FakeConnection(screen, "n/a"), provider, new AllowApproval());

        var events = await Collect(loop);

        Assert.Contains(events, e => e is ScreenshotEvent);
        Assert.True(screen.CaptureCount >= 1);
    }

    [Fact]
    public async Task WithScreen_ComputerAction_Executes()
    {
        var screen = new RecordingScreen();
        var provider = new FakeProvider(Click(), Done("done"));
        var loop = new AgentLoop(new FakeConnection(screen, "n/a"), provider, new AllowApproval());

        await Collect(loop);

        Assert.Equal(1, screen.Clicks);
    }

    [Fact]
    public async Task Switch_ToProfile_CallsConnection()
    {
        var conn = new FakeConnection(null, "no screen");
        var provider = new FakeProvider(new SwitchAction(Profile: "work"), Done("done"));
        var loop = new AgentLoop(conn, provider, new AllowApproval());

        var result = (await Collect(loop)).OfType<ToolResultEvent>().First();

        Assert.Equal("work", conn.SwitchedProfile);
        Assert.Contains("work", result.Summary);
    }

    [Fact]
    public async Task Switch_ToVm_CallsConnection()
    {
        var vm = Guid.NewGuid();
        var conn = new FakeConnection(null, "no screen");
        var provider = new FakeProvider(new SwitchAction(VmId: vm), Done("done"));
        var loop = new AgentLoop(conn, provider, new AllowApproval());

        await Collect(loop);

        Assert.Equal(vm, conn.SwitchedVm);
    }

    [Fact]
    public async Task VmAction_RoutesToConnection_WithOperationAndTarget()
    {
        var vm = Guid.NewGuid();
        var conn = new FakeConnection(null, "no screen");
        var provider = new FakeProvider(new VmAction(VmOperation.Start, vm), Done("done"));
        var loop = new AgentLoop(conn, provider, new AllowApproval());

        var result = (await Collect(loop)).OfType<ToolResultEvent>().First();

        Assert.Equal(VmOperation.Start, conn.VmOp);
        Assert.Equal(vm, conn.VmActionTarget);
        Assert.Contains("Start", result.Summary);
    }

    [Fact]
    public async Task VmAction_NullTarget_MeansCurrentVm()
    {
        var conn = new FakeConnection(new RecordingScreen(), "n/a");
        var provider = new FakeProvider(new VmAction(VmOperation.Pause), Done("done"));
        var loop = new AgentLoop(conn, provider, new AllowApproval());

        await Collect(loop);

        Assert.Equal(VmOperation.Pause, conn.VmOp);
        Assert.Null(conn.VmActionTarget);
    }

    [Fact]
    public async Task VmSetup_RoutesToConnection_WithSpecAndTarget()
    {
        var vm = Guid.NewGuid();
        var conn = new FakeConnection(null, "no screen");
        var provider = new FakeProvider(new VmSetupAction(new VmSpec(MemoryMB: 4096), vm), Done("done"));
        var loop = new AgentLoop(conn, provider, new AllowApproval());

        var result = (await Collect(loop)).OfType<ToolResultEvent>().First();

        Assert.Equal(vm, conn.VmSetupTarget);
        Assert.Equal(4096, conn.VmSetupSpec!.MemoryMB);
        Assert.Contains("ok", result.Summary);
    }

    [Fact]
    public async Task Switch_Disconnect_CallsConnection()
    {
        var conn = new FakeConnection(new RecordingScreen(), "n/a");
        var provider = new FakeProvider(new SwitchAction(Disconnect: true), Done("done"));
        var loop = new AgentLoop(conn, provider, new AllowApproval());

        var result = (await Collect(loop)).OfType<ToolResultEvent>().First();

        Assert.True(conn.Disconnected);
        Assert.Contains("disconnect", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ── Harness ────────────────────────────────────────────────────────────────

    private static ClickAction Click() => new(MouseButton.Left, 10, 20);
    private static DoneAction Done(string r) => new(r);

    private static async Task<List<AgentEvent>> Collect(AgentLoop loop)
    {
        var events = new List<AgentEvent>();
        await foreach (var e in loop.RunAsync("go", new AgentOptions(MaxSteps: 10, PostActionDelay: TimeSpan.Zero)))
            events.Add(e);
        return events;
    }

    private sealed class FakeProvider(params AgentAction[] actions) : IComputerUseProvider
    {
        private readonly Queue<AgentAction> _actions = new(actions);
        public ProviderRequest? LastRequest { get; private set; }
        public string ModelId => "fake";
        public string BuildSystemPrompt() => "fake";

        public Task<ProviderResponse> NextActionAsync(ProviderRequest request, CancellationToken ct)
        {
            LastRequest = request;
            var action = _actions.Count > 0 ? _actions.Dequeue() : new DoneAction("done");
            var toolUseId = action is DoneAction ? null : "t1";
            return Task.FromResult(new ProviderResponse(action, null, toolUseId, new TokenUsage(0, 0)));
        }
    }

    private sealed class FakeConnection(IRdpClient? screen, string observation) : IConnection
    {
        public int DescribeCalls { get; private set; }
        public string? SwitchedProfile { get; private set; }
        public Guid? SwitchedVm { get; private set; }
        public bool Disconnected { get; private set; }
        public IRdpClient? Screen { get; } = screen;
        public bool HasVmHost => false;

        public Task<string> DescribeAsync(CancellationToken ct = default)
        {
            DescribeCalls++;
            return Task.FromResult(observation);
        }

        public Task SwitchToProfileAsync(string name, CancellationToken ct = default)
        {
            SwitchedProfile = name;
            return Task.CompletedTask;
        }

        public Task SwitchToVmAsync(Guid vmId, CancellationToken ct = default)
        {
            SwitchedVm = vmId;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            Disconnected = true;
            return Task.CompletedTask;
        }

        public VmOperation? VmOp { get; private set; }
        public Guid? VmActionTarget { get; private set; }

        public Task<string> RunVmActionAsync(VmOperation operation, Guid? vmId, CancellationToken ct = default)
        {
            VmOp = operation;
            VmActionTarget = vmId;
            return Task.FromResult($"{operation} ok");
        }

        public VmSpec? VmSetupSpec { get; private set; }
        public Guid? VmSetupTarget { get; private set; }

        public Task<string> RunVmSetupAsync(VmSpec spec, Guid? vmId, CancellationToken ct = default)
        {
            VmSetupSpec = spec;
            VmSetupTarget = vmId;
            return Task.FromResult("ok");
        }
    }

    private sealed class AllowApproval : IApprovalSink
    {
        public Task<ToolDecision> RequestAsync(string tool, object payload, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult(ToolDecision.Allow);
    }

    private sealed class RecordingScreen : IRdpClient
    {
        // A real 16x16 PNG so the cursor-annotation decode/re-encode path is exercised.
        private static readonly byte[] Frame = MakePng();

        public int CaptureCount { get; private set; }
        public int Clicks { get; private set; }
        public ConnectionState State => ConnectionState.Connected;
        public (int Width, int Height) Resolution => (16, 16);
        public event EventHandler<ConnectionState>? StateChanged { add { } remove { } }

        public Task<byte[]> CaptureScreenshotPngAsync(CancellationToken ct)
        {
            CaptureCount++;
            return Task.FromResult(Frame);
        }

        private static byte[] MakePng()
        {
            using var bitmap = new SKBitmap(16, 16);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        public Task MouseClickAsync(MouseButton button, int x, int y, CancellationToken ct)
        {
            Clicks++;
            return Task.CompletedTask;
        }

        public Task ConnectAsync(ConnectOptions options, CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task MouseMoveAsync(int x, int y, CancellationToken ct) => Task.CompletedTask;
        public Task MouseScrollAsync(int x, int y, ScrollDirection direction, int amount, CancellationToken ct) => Task.CompletedTask;
        public Task KeyPressAsync(string keys, CancellationToken ct) => Task.CompletedTask;
        public Task TypeTextAsync(string text, CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
