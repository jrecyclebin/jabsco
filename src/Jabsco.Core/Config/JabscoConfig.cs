namespace Jabsco.Core.Config;

public sealed record JabscoConfig(
    string? AnthropicApiKey,
    AgentConfig Agent,
    FeatureFlags Features);

public sealed record FeatureFlags(
    bool HyperV = false);

public sealed record AgentConfig(
    string? SystemPrompt,
    int? MaxSteps,
    int? PostActionDelayMs,
    int? TimeBudgetSeconds,
    string? ToolPolicy);
