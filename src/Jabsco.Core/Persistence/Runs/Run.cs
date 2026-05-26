using Jabsco.Common.Events;

namespace Jabsco.Core.Persistence.Runs;

public sealed record Run(
    string Id,
    int? ProfileId,
    string Host,
    string Model,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Prompt,
    string? FinalResponse,
    StoppedReason? StoppedReason,
    int? Steps,
    int? InputTokens,
    int? OutputTokens,
    string TranscriptPath);
