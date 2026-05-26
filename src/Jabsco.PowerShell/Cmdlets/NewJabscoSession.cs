using System.Management.Automation;
using Jabsco.Common.Contracts;
using Jabsco.Common.Events;
using Jabsco.PowerShell.Client;

namespace Jabsco.PowerShell.Cmdlets;

[Cmdlet(VerbsCommon.New, "JabscoSession")]
[OutputType(typeof(SessionInfo))]
public sealed class NewJabscoSessionCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true)] public string HostName { get; set; } = "";
    [Parameter] public PSCredential? Credential { get; set; }
    [Parameter] public string Model { get; set; } = "claude-opus-4-7";
    [Parameter] public int IdleTimeoutSeconds { get; set; } = 300;

    protected override void ProcessRecord()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        await DaemonLauncher.EnsureRunningAsync();
        using var client = await DaemonClient.ConnectAsync();
        var req = new SessionCreateRequest(HostName, null, Model, IdleTimeoutSeconds, null);
        var resp = await client.SessionCreateAsync(req);
        WriteObject(new SessionInfo(resp.SessionId, HostName, ConnectionState.Connected, DateTimeOffset.UtcNow));
    }
}
