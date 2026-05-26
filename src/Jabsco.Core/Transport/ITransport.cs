namespace Jabsco.Core.Transport;

public interface ITransport
{
    Task<Stream> ConnectAsync(EndpointSpec endpoint, CancellationToken ct);
}

public abstract record EndpointSpec;
public sealed record TcpEndpoint(string Host, int Port = 3389) : EndpointSpec;
public sealed record HvSocketEndpoint(Guid VmId) : EndpointSpec;
public sealed record VsockEndpoint(uint Cid, uint Port) : EndpointSpec;
