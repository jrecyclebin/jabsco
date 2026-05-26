using System.Runtime.CompilerServices;
using System.Text.Json;
using Jabsco.Common.Events;
using Jabsco.Common.Json;

namespace Jabsco.Core.Transcripts;

public static class TranscriptReader
{
    public static async IAsyncEnumerable<AgentEvent> ReadAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(path);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            AgentEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize(line, JabscoJsonContext.Default.AgentEvent);
            }
            catch (JsonException)
            {
                // Malformed line — skip and continue
            }
            if (evt is not null) yield return evt;
        }
    }

    public static IAsyncEnumerable<AgentEvent> ReadForRunAsync(string runId, CancellationToken ct = default) =>
        ReadAsync(TranscriptPaths.ForRun(runId), ct);
}
