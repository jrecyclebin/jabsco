using System.Text.Json;
using Jabsco.Common.Events;
using Jabsco.Common.Json;

namespace Jabsco.Core.Transcripts;

// Appends AgentEvents as NDJSON lines. ScreenshotEvent is skipped (live stream only).
public sealed class TranscriptWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;

    public TranscriptWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8)
        {
            AutoFlush = false
        };
    }

    public static TranscriptWriter ForRun(string runId) =>
        new(TranscriptPaths.ForRun(runId));

    public async Task WriteAsync(AgentEvent evt, CancellationToken ct = default)
    {
        // Screenshots are not persisted — they're live-stream-only
        if (evt is ScreenshotEvent) return;

        var json = JsonSerializer.Serialize(evt, JabscoJsonContext.Default.AgentEvent);
        await _writer.WriteLineAsync(json.AsMemory(), ct);
        await _writer.FlushAsync(ct);
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}
