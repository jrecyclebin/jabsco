using Jabsco.Core.Config;

namespace Jabsco.Core.Agent;

public sealed record AgentOptions(
    int MaxSteps = 100,
    TimeSpan? TimeBudget = null,
    string? ToolPolicy = null,
    TimeSpan PostActionDelay = default,
    ModelStrategy ModelStrategy = ModelStrategy.LatestOnly);
