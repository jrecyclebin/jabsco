using Jabsco.Common.Events;

namespace Jabsco.Core.Providers;

public interface IComputerUseProvider
{
    string ModelId { get; }
    Task<ProviderResponse> NextActionAsync(ProviderRequest request, CancellationToken ct);
}

public sealed record ProviderRequest(
    byte[] ScreenshotPng,
    string UserPrompt,
    IReadOnlyList<ConversationTurn> History,
    IReadOnlyList<ToolTurn> CurrentTurns,
    ProviderOptions Options);

public sealed record ProviderResponse(
    AgentAction Action,
    string? Thinking,
    string? ToolUseId,
    TokenUsage Usage);

// One tool action + result within a prompt run
public sealed record ToolTurn(
    AgentAction Action,
    string Result,
    string? ToolUseId);

// A complete prompt → tool exchanges → response round
public sealed record ConversationTurn(
    string UserPrompt,
    IReadOnlyList<ToolTurn> Turns,
    string? FinalResponse);

public sealed record ProviderOptions(
    bool ExtendedThinking = false,
    int MaxTokens = 4096);

public sealed record TokenUsage(int InputTokens, int OutputTokens, int CachedInputTokens = 0);
