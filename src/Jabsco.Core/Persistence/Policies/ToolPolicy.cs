namespace Jabsco.Core.Persistence.Policies;

public sealed record ToolPolicy(
    int Id,
    string Name,
    string? Description,
    IReadOnlyList<ToolPolicyRule> Rules);
