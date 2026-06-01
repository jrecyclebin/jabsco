using Jabsco.Core.Persistence.Profiles;
using Jabsco.Core.Rdp;
using Microsoft.Extensions.Logging;

namespace Jabsco.Core.Sessions;

// Connects a real FreeRDP screen for the ConnectionController. Thin glue over FreeRdpClient.
public sealed class RdpConnector(ILoggerFactory loggerFactory) : IRdpConnector
{
    public async Task<IRdpClient> ConnectAsync(ConnectOptions options, CancellationToken ct = default)
    {
        var client = new FreeRdpClient(loggerFactory.CreateLogger<FreeRdpClient>());
        await client.ConnectAsync(options, ct);
        return client;
    }
}

// Saved connections the controller can switch to, backed by the profiles table.
public sealed class ProfileDirectory(ProfileRepository repository) : IProfileDirectory
{
    public Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct = default) =>
        repository.GetAllAsync(ct);

    // The observation labels a profile by Name when set, else by Host — so accept either,
    // since that's the identifier the model will echo back to switch.
    public async Task<Profile?> FindByNameAsync(string name, CancellationToken ct = default) =>
        (await repository.GetAllAsync(ct)).FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Host, name, StringComparison.OrdinalIgnoreCase));
}

// Used when there's no profile store — initial connect still works; agent profile switches don't.
public sealed class EmptyProfileDirectory : IProfileDirectory
{
    public Task<IReadOnlyList<Profile>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Profile>>([]);
    public Task<Profile?> FindByNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult<Profile?>(null);
}
