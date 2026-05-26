namespace Jabsco.Core.HyperV;

public sealed record HyperVVm(Guid Id, string Name, HyperVVmState State);

public enum HyperVVmState { Running, Stopped, Paused, Other }
