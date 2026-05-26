using Jabsco.Common.Events;
using Jabsco.Core.Agent;
using Jabsco.Core.Approval;
using Jabsco.Core.Providers;
using Jabsco.Core.Rdp;
using Microsoft.Extensions.Logging;

namespace Jabsco.Core.Sessions;

public sealed class RdpSession : ISession
{
    private readonly IRdpClient _rdp;
    private readonly AgentLoop _agentLoop;
    private readonly ILogger<RdpSession> _logger;
    private CancellationTokenSource? _promptCts;
    private readonly SemaphoreSlim _promptLock = new(1, 1);

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Host { get; }

    public ConnectionState State => _rdp.State;

    public RdpSession(
        IRdpClient rdp,
        IComputerUseProvider provider,
        IApprovalSink approval,
        ILogger<RdpSession> logger)
    {
        _rdp = rdp;
        _logger = logger;
        _agentLoop = new AgentLoop(rdp, provider, approval);

        // Expose the host from the RDP client's connection options via the StateChanged event
        // connection info — stored when ConnectAsync was called. We read it from the rdp instance.
        // Since IRdpClient doesn't expose Host directly, we default to empty and set it from factory.
        Host = string.Empty;
    }

    // Constructor used by SessionFactory, which knows the host.
    internal RdpSession(
        IRdpClient rdp,
        IComputerUseProvider provider,
        IApprovalSink approval,
        ILogger<RdpSession> logger,
        string host) : this(rdp, provider, approval, logger)
    {
        Host = host;
    }

    public async IAsyncEnumerable<AgentEvent> PromptAsync(
        string prompt,
        PromptOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await _promptLock.WaitAsync(ct);
        var opts = options ?? new PromptOptions();
        var agentOpts = new AgentOptions(opts.MaxSteps, opts.TimeBudget, opts.ToolPolicy,
            opts.PostActionDelay ?? TimeSpan.FromMilliseconds(800));

        _promptCts?.Dispose();
        _promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var localCt = _promptCts.Token;
        _promptLock.Release();

        _logger.LogInformation("Session {Id}: prompt started", Id);
        await foreach (var evt in _agentLoop.RunAsync(prompt, agentOpts, ct: localCt))
            yield return evt;

        _logger.LogInformation("Session {Id}: prompt finished", Id);
    }

    public async Task CancelAsync(CancelMode mode, CancellationToken ct = default)
    {
        if (_promptCts is null) return;

        _promptCts.Cancel();

        if (mode == CancelMode.Graceful)
        {
            // Give the loop up to 2s to drain
            try
            {
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException) { /* caller cancelled the wait */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _promptCts?.Cancel();
        _promptCts?.Dispose();
        _promptLock.Dispose();

        if (_rdp.State != ConnectionState.Disconnected)
            await _rdp.DisconnectAsync(CancellationToken.None);

        await _rdp.DisposeAsync();
    }
}
