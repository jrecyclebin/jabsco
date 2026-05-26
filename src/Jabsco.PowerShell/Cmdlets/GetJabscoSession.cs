using System.Management.Automation;
using Jabsco.Common.Contracts;
using Jabsco.PowerShell.Client;

namespace Jabsco.PowerShell.Cmdlets;

[Cmdlet(VerbsCommon.Get, "JabscoSession")]
[OutputType(typeof(SessionInfo))]
public sealed class GetJabscoSessionCmdlet : PSCmdlet
{
    protected override void ProcessRecord()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        await DaemonLauncher.EnsureRunningAsync();
        using var client = await DaemonClient.ConnectAsync();
        var sessions = await client.SessionListAsync();
        foreach (var s in sessions) WriteObject(s);
    }
}
