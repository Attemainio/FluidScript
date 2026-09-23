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
