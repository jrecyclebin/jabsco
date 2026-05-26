namespace Jabsco.Daemon.State;

public sealed class ConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim _sem;

    public ConcurrencyGate(int max = 4) => _sem = new SemaphoreSlim(max, max);

    // Returns true if a slot was acquired, false if all slots are in use.
    public Task<bool> TryAcquireAsync(CancellationToken ct) =>
        _sem.WaitAsync(0, ct).ContinueWith(t => t.Result, TaskScheduler.Default);

    public void Release() => _sem.Release();
    public void Dispose() => _sem.Dispose();
}
