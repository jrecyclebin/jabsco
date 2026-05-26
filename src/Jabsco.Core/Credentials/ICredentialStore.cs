using System.Net;

namespace Jabsco.Core.Credentials;

public interface ICredentialStore
{
    Task<NetworkCredential?> GetAsync(string credentialRef, CancellationToken ct);
    Task SetAsync(string credentialRef, NetworkCredential credential, CancellationToken ct);
    Task DeleteAsync(string credentialRef, CancellationToken ct);
}
