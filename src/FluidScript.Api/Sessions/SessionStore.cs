using System.Collections.Concurrent;

namespace FluidScript.Api.Sessions;

/// <summary>A concurrent dictionary of independent sessions; the one piece of state requests share.</summary>
public sealed class SessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<SessionKey, Session> _sessions = new();

    /// <inheritdoc />
    public int Count => _sessions.Count;

    /// <inheritdoc />
    public Session GetOrCreate(SessionKey key) => _sessions.GetOrAdd(key, static k => new Session(k));

    /// <inheritdoc />
    public int Evict(TimeSpan idleFor)
    {
        var cutoff = DateTimeOffset.UtcNow - idleFor;
        var dropped = 0;

        foreach (var (key, session) in _sessions)
        {
            if (session.LastTouched < cutoff && !session.HasDraftInFlight && _sessions.TryRemove(key, out _))
            {
                dropped++;
            }
        }

        return dropped;
    }

    /// <inheritdoc />
    public void Clear() => _sessions.Clear();
}
