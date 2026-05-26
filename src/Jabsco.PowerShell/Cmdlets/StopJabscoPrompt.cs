using System.Management.Automation;
using Jabsco.Common.Contracts;
using Jabsco.PowerShell.Client;

namespace Jabsco.PowerShell.Cmdlets;

[Cmdlet(VerbsLifecycle.Stop, "JabscoPrompt")]
public sealed class StopJabscoPromptCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true)]
    public string SessionId { get; set; } = "";

    protected override void ProcessRecord()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        using var client = await DaemonClient.ConnectAsync();
        await client.SessionCancelAsync(new SessionCancelRequest(SessionId, "graceful"));
    }
}
