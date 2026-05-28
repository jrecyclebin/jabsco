namespace Jabsco.Core.Providers.Gemini;

public sealed record GeminiOptions(
    string ApiKey,
    string Model = "gemini-3-flash-preview",
    int DisplayWidth = 1280,
    int DisplayHeight = 800,
    string? SystemPrompt = null);
