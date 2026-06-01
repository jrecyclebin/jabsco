using Jabsco.Common.Events;
using Jabsco.Core.Config;

namespace Jabsco.Core.Providers;

public interface IComputerUseProvider
{
    string ModelId { get; }
    string BuildSystemPrompt();
    Task<ProviderResponse> NextActionAsync(ProviderRequest request, CancellationToken ct);
}

public sealed record ProviderRequest(
    string UserPrompt,
    IReadOnlyList<ConversationTurn> History,
    IReadOnlyList<ToolTurn> CurrentTurns,
    ProviderOptions Options,
    byte[]? PromptScreenshotPng = null,
    string? Observation = null); // text shown in place of a screenshot when there's no screen

public sealed record ProviderResponse(
    AgentAction Action,
    string? Thinking,
    string? ToolUseId,
    TokenUsage Usage);

// One tool action + result within a prompt run. ScreenshotPng holds the screen
// captured after the action, kept only for screenshot actions under ModelManaged.
public sealed record ToolTurn(
    AgentAction Action,
    string Result,
    string ToolUseId,
    byte[]? ScreenshotPng = null);

// A complete prompt → tool exchanges → response round. PromptScreenshotPng is the screen
// shown with the user prompt; per-turn screenshots live on the turns themselves.
public sealed record ConversationTurn(
    string UserPrompt,
    IReadOnlyList<ToolTurn> Turns,
    string? FinalResponse,
    byte[]? PromptScreenshotPng = null);

public sealed record ProviderOptions(
    int MaxTokens = 4096,
    ModelStrategy Strategy = ModelStrategy.CacheAware,
    bool HasScreen = true,
    bool HasVmHost = false);

public sealed record TokenUsage(int InputTokens, int OutputTokens, int CachedInputTokens = 0);
