using Jabsco.Core.Config;

namespace Jabsco.Core.Providers.Claude;

public sealed record ClaudeOptions(
    string ApiKey,
    string Model = "claude-sonnet-4-6",
    int DisplayWidth = 1280,
    int DisplayHeight = 800,
    ThinkingMode Thinking = ThinkingMode.Low,
    string? SystemPrompt = null);
