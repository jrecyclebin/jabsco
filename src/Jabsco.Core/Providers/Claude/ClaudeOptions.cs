namespace Jabsco.Core.Providers.Claude;

public sealed record ClaudeOptions(
    string ApiKey,
    string Model = "claude-opus-4-7",
    int DisplayWidth = 1280,
    int DisplayHeight = 800,
    bool ExtendedThinking = false,
    string? SystemPrompt = null);
