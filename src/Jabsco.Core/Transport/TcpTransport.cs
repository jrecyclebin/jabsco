namespace Jabsco.Core.Transport;

// Transport connectivity is handled internally by FreeRDP. This stub exists
// for future use if we need to wrap or intercept the socket layer.
public sealed class TcpTransport : ITransport
{
    public Task<Stream> ConnectAsync(EndpointSpec endpoint, CancellationToken ct) =>
        throw new NotImplementedException("TCP transport is handled internally by FreeRDP");
}
