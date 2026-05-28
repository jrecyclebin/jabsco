namespace Jabsco.Core.VmHost;

public sealed record VmInfo(Guid Id, string Name, VmState State);

public enum VmState { Running, Stopped, Paused, Other }

public interface IVmHost
{
    Task<IReadOnlyList<VmInfo>> ListVmsAsync(CancellationToken ct = default);
}
