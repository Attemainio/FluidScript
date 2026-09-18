using System.Collections.Concurrent;

namespace FluidScript.Api.Sessions;

/// <summary>The shared cache of sessions (<c>41</c>).</summary>
public interface ISessionStore
{
    /// <summary>How many sessions are held.</summary>
    int Count { get; }

    /// <summary>Gets or creates the session for a key. An unknown key is created, never refused: sessions are a cache.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The session.</returns>
    Session GetOrCreate(SessionKey key);

    /// <summary>Drops sessions untouched for longer than a timeout and with no draft in flight.</summary>
    /// <param name="idleFor">The timeout.</param>
    /// <returns>How many were dropped.</returns>
    int Evict(TimeSpan idleFor);

    /// <summary>Drops every session. No response changes (<c>41</c>'s invariant 2); the next solve per client is cold.</summary>
    void Clear();
}

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
