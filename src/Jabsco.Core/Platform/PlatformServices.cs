using Jabsco.Core.Approval;
using Jabsco.Core.Credentials;
using Jabsco.Core.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Jabsco.Core.Platform;

public static class PlatformServices
{
    public static IServiceCollection Register(IServiceCollection services)
    {
        services.AddSingleton<PolicyMatcher>();
        services.AddSingleton<SessionFactory>();

        // Register the platform-appropriate credential store
        if (OperatingSystem.IsWindows())
            services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
        else if (OperatingSystem.IsLinux())
            services.AddSingleton<ICredentialStore, LibsecretCredentialStore>();
        else if (OperatingSystem.IsMacOS())
            services.AddSingleton<ICredentialStore, MacosCredentialStore>();
        else
            throw new PlatformNotSupportedException("Jabsco requires Windows, Linux, or macOS");

        return services;
    }
}
