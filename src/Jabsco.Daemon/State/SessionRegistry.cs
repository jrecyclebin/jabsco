using System.Collections.Concurrent;
using Jabsco.Common.Contracts;
using Jabsco.Common.Events;
using Jabsco.Core.Sessions;

namespace Jabsco.Daemon.State;

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();

    public void Add(string id, ISession session) =>
        _sessions[id] = new SessionEntry(id, session, DateTimeOffset.UtcNow);

    public SessionEntry? Get(string id) => _sessions.TryGetValue(id, out var e) ? e : null;

    public IReadOnlyList<SessionInfo> List() => _sessions.Values
        .Select(e => new SessionInfo(e.Id, e.Session.Host, e.Session.State, e.LastActivity))
        .ToList();

    public bool Remove(string id) => _sessions.TryRemove(id, out _);
}

public sealed record SessionEntry(string Id, ISession Session, DateTimeOffset LastActivity)
{
    public DateTimeOffset LastActivity { get; set; } = LastActivity;
}
