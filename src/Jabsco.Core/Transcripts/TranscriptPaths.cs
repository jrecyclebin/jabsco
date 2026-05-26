using Jabsco.Core.Platform;

namespace Jabsco.Core.Transcripts;

public static class TranscriptPaths
{
    public static string ForRun(string runId) =>
        Path.Combine(KnownPaths.StateDir, "transcripts", $"{runId}.jsonl");
}
