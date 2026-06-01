using Jabsco.Common.Events;

namespace Jabsco.Core.VmHost;

public sealed record VmInfo(Guid Id, string Name, VmState State);

public enum VmState { Running, Stopped, Paused, Other }

public interface IVmHost
{
    Task<IReadOnlyList<VmInfo>> ListVmsAsync(CancellationToken ct = default);
    Task ChangeStateAsync(Guid vmId, VmOperation operation, CancellationToken ct = default);

    // Create a new VM from the spec, returning its id. Alter changes only the provided fields.
    Task<Guid> CreateVmAsync(VmSpec spec, CancellationToken ct = default);
    Task AlterVmAsync(Guid vmId, VmSpec spec, CancellationToken ct = default);
}
