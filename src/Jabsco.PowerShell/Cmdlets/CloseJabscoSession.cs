using System.Management.Automation;
using Jabsco.PowerShell.Client;

namespace Jabsco.PowerShell.Cmdlets;

[Cmdlet(VerbsCommon.Close, "JabscoSession")]
public sealed class CloseJabscoSessionCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true)]
    public string SessionId { get; set; } = "";

    protected override void ProcessRecord()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        await DaemonLauncher.EnsureRunningAsync();
        using var client = await DaemonClient.ConnectAsync();
        await client.SessionCloseAsync(SessionId);
    }
}
