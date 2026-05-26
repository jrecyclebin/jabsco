using Jabsco.Common.Contracts;
using Jabsco.Core.Sessions;
using Jabsco.Daemon.State;
using StreamJsonRpc;

namespace Jabsco.Daemon.Rpc;

public sealed class RpcTarget
{
    private readonly SessionRegistry _registry;
    private readonly ConcurrencyGate _gate;
    private readonly IServiceProvider _services;
    private static readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public RpcTarget(SessionRegistry registry, ConcurrencyGate gate, IServiceProvider services)
    {
        _registry = registry;
        _gate = gate;
        _services = services;
    }

    [JsonRpcMethod(RpcMethods.SessionCreate)]
    public Task<SessionCreateResponse> SessionCreate(SessionCreateRequest request)
    {
        // TODO: construct real ISession via IRdpClient + AgentLoop wiring
        var id = Guid.NewGuid().ToString("N");
        return Task.FromResult(new SessionCreateResponse(id));
    }

    [JsonRpcMethod(RpcMethods.SessionList)]
    public IReadOnlyList<SessionInfo> SessionList() => _registry.List();

    [JsonRpcMethod(RpcMethods.SessionCancel)]
    public async Task SessionCancel(SessionCancelRequest request)
    {
        var entry = _registry.Get(request.SessionId)
            ?? throw new InvalidOperationException($"Session {request.SessionId} not found");
        var mode = request.Mode == "immediate" ? CancelMode.Immediate : CancelMode.Graceful;
        await entry.Session.CancelAsync(mode);
    }

    [JsonRpcMethod(RpcMethods.SessionClose)]
    public async Task SessionClose(string sessionId)
    {
        var entry = _registry.Get(sessionId)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");
        _registry.Remove(sessionId);
        await entry.Session.DisposeAsync();
    }

    [JsonRpcMethod(RpcMethods.DaemonStatus)]
    public DaemonStatusResponse DaemonStatus() => new(
        (long)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds,
        _registry.List().Count,
        "1.0.0");

    [JsonRpcMethod(RpcMethods.DaemonShutdown)]
    public void DaemonShutdown()
    {
        // TODO: inject IHostApplicationLifetime and call StopApplication
    }
}
