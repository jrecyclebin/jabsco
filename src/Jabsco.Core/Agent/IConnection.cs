using Jabsco.Common.Events;
using Jabsco.Core.Rdp;

namespace Jabsco.Core.Agent;

// The live connection the agent loop drives. Screen is null when there's nothing to look at
// (disconnected, or on a VM host with no VM connected); DescribeAsync then supplies the text
// the model sees in place of a screenshot.
public interface IConnection
{
    IRdpClient? Screen { get; }
    bool HasVmHost { get; }
    Task<string> DescribeAsync(CancellationToken ct = default);

    Task SwitchToProfileAsync(string name, CancellationToken ct = default);
    Task SwitchToVmAsync(Guid vmId, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    // Perform a VM lifecycle action on the host. A null vmId targets the connected VM.
    Task<string> RunVmActionAsync(VmOperation operation, Guid? vmId, CancellationToken ct = default);

    // Create (vmId null) or alter (vmId set) a VM on the host.
    Task<string> RunVmSetupAsync(VmSpec spec, Guid? vmId, CancellationToken ct = default);
}

// A connection that is just one live screen — no host, no switching. Backs a plain RDP
// session until the full ConnectionController is wired through the session/UI.
public sealed class ScreenConnection(IRdpClient screen) : IConnection
{
    public IRdpClient? Screen { get; } = screen;
    public bool HasVmHost => false;
    public Task<string> DescribeAsync(CancellationToken ct = default) => Task.FromResult(string.Empty);

    public Task SwitchToProfileAsync(string name, CancellationToken ct = default) => NotSupported();
    public Task SwitchToVmAsync(Guid vmId, CancellationToken ct = default) => NotSupported();
    public Task DisconnectAsync(CancellationToken ct = default) => NotSupported();
    public Task<string> RunVmActionAsync(VmOperation operation, Guid? vmId, CancellationToken ct = default) =>
        throw new NotSupportedException("This session has no Hyper-V host.");
    public Task<string> RunVmSetupAsync(VmSpec spec, Guid? vmId, CancellationToken ct = default) =>
        throw new NotSupportedException("This session has no Hyper-V host.");

    private static Task NotSupported() =>
        throw new NotSupportedException("This session can't switch connections.");
}
