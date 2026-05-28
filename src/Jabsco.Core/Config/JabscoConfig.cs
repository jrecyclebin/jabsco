namespace Jabsco.Core.Config;

public enum ModelStrategy { LatestOnly, CacheAware }

// Adaptive thinking effort level sent to the model. Low and High map directly to the effort strings.
public enum ThinkingMode { Off, Low, High }

public sealed record JabscoConfig(
    string? AnthropicApiKey,
    string? GeminiApiKey,
    string? ModelId,
    AgentConfig Agent,
    FeatureFlags Features);

public sealed record FeatureFlags(
    bool HyperV = false);

public sealed record AgentConfig(
    string? SystemPrompt,
    int? MaxSteps,
    int? PostActionDelayMs,
    int? TimeBudgetSeconds,
    string? ToolPolicy,
    ModelStrategy? ModelStrategy = null,
    ThinkingMode? Thinking = null);
