namespace Jabsco.Core.Persistence.Policies;

public sealed record ToolPolicyRule(
    int Id,
    int PolicyId,
    string Tool,
    ToolDecision Decision,
    string? Pattern);
