using Jabsco.Core.Approval;
using Jabsco.Core.Persistence.Profiles;
using Jabsco.Core.Providers;
using Jabsco.Core.Rdp;
using Microsoft.Extensions.Logging;

namespace Jabsco.Core.Sessions;

public sealed class SessionFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IApprovalSink _approval;

    public SessionFactory(ILoggerFactory loggerFactory, IApprovalSink approval)
    {
        _loggerFactory = loggerFactory;
        _approval = approval;
    }

    public async Task<ISession> CreateAsync(
        Profile profile,
        IComputerUseProvider provider,
        CancellationToken ct = default)
    {
        var rdp = new FreeRdpClient(_loggerFactory.CreateLogger<FreeRdpClient>());
        var opts = ConnectOptions.FromProfile(profile);
        await rdp.ConnectAsync(opts, ct);
        return new RdpSession(rdp, provider, _approval, _loggerFactory.CreateLogger<RdpSession>(), profile.Host);
    }
}
