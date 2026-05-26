namespace Jabsco.Core.Transport;

// Hyper-V socket transport — Windows-only. Transport connectivity is handled
// internally by FreeRDP. This stub exists for future use.
public sealed class HvSocketTransport : ITransport
{
    public Task<Stream> ConnectAsync(EndpointSpec endpoint, CancellationToken ct) =>
        throw new NotImplementedException("HvSocket transport is handled internally by FreeRDP");
}
