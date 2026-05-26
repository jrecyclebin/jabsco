using System.Management.Automation;
using Jabsco.PowerShell.Client;

namespace Jabsco.PowerShell.Cmdlets;

[Cmdlet(VerbsCommunications.Send, "JabscoPrompt")]
public sealed class SendJabscoPromptCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true)]
    public string SessionId { get; set; } = "";

    [Parameter(Mandatory = true, Position = 0)]
    public string Prompt { get; set; } = "";

    [Parameter] public string? ToolPolicy { get; set; }

    protected override void ProcessRecord()
    {
        // TODO: stream events back via daemon session.prompt method once
        // the daemon supports SSE or chunked JSON-RPC notifications.
        WriteVerbose($"Sending prompt to session {SessionId}");
        throw new NotImplementedException(
            "Send-JabscoPrompt not yet implemented — daemon session.prompt streaming is pending");
    }
}
