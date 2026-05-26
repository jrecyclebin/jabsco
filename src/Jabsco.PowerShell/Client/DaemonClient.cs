using System.IO.Pipes;
using Jabsco.Common.Contracts;
using StreamJsonRpc;

namespace Jabsco.PowerShell.Client;

// Connects to the Jabsco daemon over a named pipe and exposes typed RPC methods.
public sealed class DaemonClient : IDisposable
{
    internal const string PipeName = "jabsco";
    private NamedPipeClientStream? _pipe;
    private JsonRpc? _rpc;

    private DaemonClient() { }

    public static async Task<DaemonClient> ConnectAsync(CancellationToken ct = default)
    {
        var client = new DaemonClient();
        await client.ConnectInternalAsync(ct);
        return client;
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(5000, ct);
        _rpc = new JsonRpc(new HeaderDelimitedMessageHandler(_pipe));
        _rpc.StartListening();
    }

    public Task<SessionCreateResponse> SessionCreateAsync(SessionCreateRequest req, CancellationToken ct = default) =>
        _rpc!.InvokeWithCancellationAsync<SessionCreateResponse>(
            RpcMethods.SessionCreate, new object[] { req }, ct);

    public Task<IReadOnlyList<SessionInfo>> SessionListAsync(CancellationToken ct = default) =>
        _rpc!.InvokeWithCancellationAsync<IReadOnlyList<SessionInfo>>(
            RpcMethods.SessionList, null, ct);

    public Task SessionCancelAsync(SessionCancelRequest req, CancellationToken ct = default) =>
        _rpc!.InvokeWithCancellationAsync(
            RpcMethods.SessionCancel, new object[] { req }, ct);

    public Task SessionCloseAsync(string sessionId, CancellationToken ct = default) =>
        _rpc!.InvokeWithCancellationAsync(
            RpcMethods.SessionClose, new object[] { sessionId }, ct);

    public Task<DaemonStatusResponse> DaemonStatusAsync(CancellationToken ct = default) =>
        _rpc!.InvokeWithCancellationAsync<DaemonStatusResponse>(
            RpcMethods.DaemonStatus, null, ct);

    public void Dispose()
    {
        _rpc?.Dispose();
        _pipe?.Dispose();
    }
}
