using Jabsco.Common.Events;

namespace Jabsco.Core.Sessions;

public interface ISession : IAsyncDisposable
{
    string Id { get; }
    ConnectionState State { get; }
    string Host { get; }
    IAsyncEnumerable<AgentEvent> PromptAsync(string prompt, PromptOptions? options = null, CancellationToken ct = default);
    Task CancelAsync(CancelMode mode, CancellationToken ct = default);
}

public sealed record PromptOptions(
    int MaxSteps = 100,
    string? ToolPolicy = null,
    TimeSpan? TimeBudget = null,
    TimeSpan? PostActionDelay = null); // null = use default (800ms)

public enum CancelMode { Graceful, Immediate }
