namespace Jabsco.Common.Events;

public sealed record AgentStats(
    int Steps,
    long DurationMs,
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens,
    string Model,
    StoppedReason StoppedReason);
