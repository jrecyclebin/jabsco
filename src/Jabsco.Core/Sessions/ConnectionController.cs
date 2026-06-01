using System.Text;
using Jabsco.Common.Events;
using Jabsco.Core.Agent;
using Jabsco.Core.Persistence.Profiles;
using Jabsco.Core.Rdp;
using Jabsco.Core.VmHost;

namespace Jabsco.Core.Sessions;

public enum ConnectionKind { Disconnected, Rdp, VmHost, Vm }

// A Hyper-V host the session manages: the address and creds used both to query WMI
// and to build per-VM VMConnect options.
public sealed record HostConnection(string Address, string? Username, string? Password);

public interface IRdpConnector
{
    Task<IRdpClient> ConnectAsync(ConnectOptions options, CancellationToken ct = default);
}

public interface IProfileDirectory
{
    Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct = default);
    Task<Profile?> FindByNameAsync(string name, CancellationToken ct = default);
}

// Owns the live connection beneath a session. The session's chat history lives above this,
// so the controller can swap the RDP screen and host context freely without touching it.
// Kind is derived purely from whether a screen and/or a VM host are currently present.
public sealed class ConnectionController : IConnection, IAsyncDisposable
{
    private readonly IRdpConnector _connector;
    private readonly IProfileDirectory _profiles;
    private readonly Func<Profile, Task<string?>> _passwordLookup;
    private readonly Func<HostConnection, IVmHost> _hostFactory;
    private HostConnection? _host;
    private Guid? _currentVmId;
    private IReadOnlyList<VmInfo> _cachedVms = [];

    public ConnectionController(
        IRdpConnector connector,
        IProfileDirectory profiles,
        Func<Profile, Task<string?>> passwordLookup,
        Func<HostConnection, IVmHost> hostFactory)
    {
        _connector = connector;
        _profiles = profiles;
        _passwordLookup = passwordLookup;
        _hostFactory = hostFactory;
    }

    public IRdpClient? Screen { get; private set; }
    public IVmHost? VmHost { get; private set; }
    public Guid? CurrentVmId => _currentVmId;

    public bool HasScreen => Screen is not null;
    public bool HasVmHost => VmHost is not null;

    public ConnectionKind Kind => (HasScreen, HasVmHost) switch
    {
        (false, false) => ConnectionKind.Disconnected,
        (true,  false) => ConnectionKind.Rdp,
        (false, true ) => ConnectionKind.VmHost,
        (true,  true ) => ConnectionKind.Vm,
    };

    // The cached VM list — the same view the model's observation uses. Refreshed on entering
    // the host view and after a vm_action, so the UI can read it without its own WMI calls.
    public IReadOnlyList<VmInfo> Vms => _cachedVms;

    public Task<IReadOnlyList<Profile>> ListProfilesAsync(CancellationToken ct = default) =>
        _profiles.ListAsync(ct);

    public Task<IReadOnlyList<VmInfo>> ListVmsAsync(CancellationToken ct = default) =>
        VmHost?.ListVmsAsync(ct) ?? Task.FromResult<IReadOnlyList<VmInfo>>([]);

    // Initial connect from explicit inputs (carrying the live, just-typed credentials).
    // SwitchToProfileAsync is for later agent-driven switches to saved profiles.
    public async Task StartRdpAsync(ConnectOptions options, CancellationToken ct = default)
    {
        var screen = await _connector.ConnectAsync(options, ct);
        await ReplaceScreenAsync(screen);
        _host = null;
        VmHost = null;
        _currentVmId = null;
    }

    // Begin a Hyper-V session. With a vmId we connect that VM (screen + host); without one we
    // land on the host view (host, no screen). The host's live creds are retained for VM switches.
    public async Task StartHostAsync(HostConnection host, Guid? vmId, CancellationToken ct = default)
    {
        _host = host;
        VmHost = _hostFactory(host);
        if (vmId is { } id)
        {
            var screen = await _connector.ConnectAsync(VmConnectOptions(host, id), ct);
            _currentVmId = id;
            await ReplaceScreenAsync(screen);
        }
        else
        {
            await ReplaceScreenAsync(null);
            _currentVmId = null;
            await RefreshVmsAsync(ct);
        }
    }

    // The universal entry point: a saved profile resolves to one of the four states by its
    // transport and vm_id. A 'hvsocket' profile is Hyper-V — with a vm_id it's a VM (screen +
    // host), without one it's host management (host, no screen). Anything else is plain RDP.
    public async Task SwitchToProfileAsync(string name, CancellationToken ct = default)
    {
        var profile = await _profiles.FindByNameAsync(name, ct)
            ?? throw new InvalidOperationException($"No profile named '{name}'.");

        if (!IsHyperV(profile))
        {
            var opts = ConnectOptions.FromProfile(profile, await _passwordLookup(profile));
            var screen = await _connector.ConnectAsync(opts, ct);
            await ReplaceScreenAsync(screen);
            _host = null;
            VmHost = null;
            _currentVmId = null;
            return;
        }

        var host = new HostConnection(profile.Host, profile.Username, await _passwordLookup(profile));
        if (profile.VmId is { } vmId)
        {
            var screen = await _connector.ConnectAsync(VmConnectOptions(host, vmId), ct);
            _host = host;
            VmHost = _hostFactory(host);
            _currentVmId = vmId;
            await ReplaceScreenAsync(screen);
        }
        else
        {
            await ReplaceScreenAsync(null);
            _host = host;
            VmHost = _hostFactory(host);
            _currentVmId = null;
            await RefreshVmsAsync(ct);
        }
    }

    // Connect a VM on the current host via VMConnect. The host is retained.
    public async Task SwitchToVmAsync(Guid vmId, CancellationToken ct = default)
    {
        if (_host is null) throw new InvalidOperationException("Not connected to a Hyper-V host.");
        var screen = await _connector.ConnectAsync(VmConnectOptions(_host, vmId), ct);
        _currentVmId = vmId;
        await ReplaceScreenAsync(screen);
    }

    private static bool IsHyperV(Profile p) =>
        string.Equals(p.Transport, "hvsocket", StringComparison.OrdinalIgnoreCase);

    private static ConnectOptions VmConnectOptions(HostConnection host, Guid vmId) => new(
        Host: host.Address,
        Username: host.Username,
        Password: host.Password,
        VmId: vmId,
        Transport: TransportKind.HvSocket,
        AcceptAnyCertificate: true);

    // Drop the screen. Returns to the VM-host view if a host is set, else fully Disconnected.
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await ReplaceScreenAsync(null);
        _currentVmId = null;
        if (Kind == ConnectionKind.VmHost) await RefreshVmsAsync(ct);
    }

    // Perform a VM lifecycle action on the host, then refresh the cached list so the next
    // observation reflects the new state. A null vmId targets the connected VM.
    public async Task<string> RunVmActionAsync(VmOperation operation, Guid? vmId, CancellationToken ct = default)
    {
        if (VmHost is null) throw new InvalidOperationException("Not connected to a Hyper-V host.");
        var target = vmId ?? _currentVmId
            ?? throw new InvalidOperationException("No VM is connected; provide a vm_id.");
        await VmHost.ChangeStateAsync(target, operation, ct);
        await RefreshVmsAsync(ct);
        return $"{operation.ToToken()} requested for VM {target}.";
    }

    // Create (vmId null) or alter (vmId set) a VM, then refresh the cached list. Hardware-class
    // changes to a running VM are refused — the model must stop it first (no auto-stop).
    public async Task<string> RunVmSetupAsync(VmSpec spec, Guid? vmId, CancellationToken ct = default)
    {
        if (VmHost is null) throw new InvalidOperationException("Not connected to a Hyper-V host.");

        if (vmId is { } target)
        {
            if (spec.RequiresStoppedVm && _cachedVms.Any(v => v.Id == target && v.State == VmState.Running))
                throw new InvalidOperationException(
                    "That change needs the VM powered off. Stop the VM first, then alter it.");
            await VmHost.AlterVmAsync(target, spec, ct);
            await RefreshVmsAsync(ct);
            return $"Altered VM {target}.";
        }

        if (string.IsNullOrWhiteSpace(spec.Name))
            throw new InvalidOperationException("Creating a VM needs a name.");
        var created = await VmHost.CreateVmAsync(spec, ct);
        await RefreshVmsAsync(ct);
        return $"Created VM '{spec.Name}' ({created}).";
    }

    // The text the model sees with no screen. Re-queries the host first so async state changes
    // (a VM finishing start-up after a vm_action) show up — model steps are seconds apart, so a
    // per-step query is fine, and it's the only way the model can watch a VM come up.
    public async Task<string> DescribeAsync(CancellationToken ct = default)
    {
        await RefreshVmsAsync(ct);
        var profiles = await _profiles.ListAsync(ct);
        return Observation.Build(Kind, _host?.Address, _currentVmId, profiles, _cachedVms);
    }

    // Refresh the cached VM list from the host. Public so the UI can poll it for the host view.
    public async Task RefreshVmsAsync(CancellationToken ct = default)
    {
        _cachedVms = VmHost is null ? [] : await VmHost.ListVmsAsync(ct);
    }

    private async Task ReplaceScreenAsync(IRdpClient? next)
    {
        if (Screen is not null) await Screen.DisposeAsync();
        Screen = next;
    }

    public async ValueTask DisposeAsync() => await ReplaceScreenAsync(null);
}

// Formats the no-screen observation. Pure so it can be unit-tested directly.
public static class Observation
{
    public static string Build(
        ConnectionKind kind, string? hostAddress, Guid? currentVmId,
        IReadOnlyList<Profile> profiles, IReadOnlyList<VmInfo> vms)
    {
        var sb = new StringBuilder();
        sb.AppendLine("No screen is connected — here is the current state.");
        sb.AppendLine();
        sb.Append("Connection: ");
        sb.AppendLine(kind switch
        {
            ConnectionKind.VmHost => $"Hyper-V host {hostAddress} (no VM connected).",
            _                     => "disconnected."
        });

        if (profiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Saved connections (use switch with \"profile\"):");
            foreach (var p in profiles)
                sb.AppendLine($"- \"{p.Name ?? p.Host}\" — {DescribeProfile(p)}");
        }

        if (vms.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("VMs on this host (use switch with \"vm_id\", or vm_action):");
            foreach (var vm in vms)
            {
                var marker = vm.Id == currentVmId ? " (current)" : string.Empty;
                sb.AppendLine($"- {vm.Name} — {vm.State} — {vm.Id}{marker}");
            }
            sb.AppendLine();
            sb.AppendLine("VM state changes take a few seconds — after a vm_action, use wait, then read this list again to confirm.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string DescribeProfile(Profile p) =>
        string.Equals(p.Transport, "hvsocket", StringComparison.OrdinalIgnoreCase)
            ? (p.VmId is null ? $"Hyper-V host {p.Host}" : $"VM on {p.Host}")
            : $"RDP {p.Host}";
}
