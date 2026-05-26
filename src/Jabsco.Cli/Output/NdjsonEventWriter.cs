using System.Text.Json;
using Jabsco.Common.Events;
using Jabsco.Common.Json;

namespace Jabsco.Cli.Output;

public sealed class NdjsonEventWriter
{
    private readonly TextWriter _out;
    private readonly bool _quiet;

    public NdjsonEventWriter(TextWriter output, bool quiet = false)
    {
        _out = output;
        _quiet = quiet;
    }

    public async Task WriteAsync(AgentEvent ev)
    {
        if (_quiet && ev is not FinalEvent and not ErrorEvent) return;
        var json = JsonSerializer.Serialize(ev, JabscoJsonContext.Default.AgentEvent);
        await _out.WriteLineAsync(json);
    }

    public async Task StreamAsync(IAsyncEnumerable<AgentEvent> events, CancellationToken ct = default)
    {
        await foreach (var ev in events.WithCancellation(ct))
        {
            await WriteAsync(ev);
            if (ev is FinalEvent or ErrorEvent) break;
        }
    }
}
