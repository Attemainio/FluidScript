using FluidScript.Core.Solvers;

namespace FluidScript.Api.Sessions;

/// <summary>What a session is keyed by: the REST major and the client's id, so warm starts never cross majors (<c>42</c>).</summary>
/// <param name="ApiMajor">The REST major the request came in on.</param>
/// <param name="SessionId">The client's opaque id.</param>
public readonly record struct SessionKey(int ApiMajor, string SessionId);

/// <summary>Per-client state: the last solution, for warm starting, and the draft solve in flight, for supersession (<c>41</c>).</summary>
/// <remarks>
/// <para>
/// A session is a cache, never a source of truth. The script arrives with every request; losing the
/// session costs one cold solve and nothing else, which is what lets the store evict freely and a
/// restart go unnoticed.
/// </para>
/// <para>
/// Supersession is the load-bearing part. Typing produces one request per debounce interval, each
/// making the last one pointless, so a new draft cancels the draft before it and at most one solve
/// runs per session. A transient run is never registered here (<c>D-22</c>): draft requests cancel
/// only other draft requests.
/// </para>
/// </remarks>
public sealed class Session(SessionKey key)
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _inFlight;

    /// <summary>The key.</summary>
    public SessionKey Key { get; } = key;

    /// <summary>When the session was last used, for eviction.</summary>
    public DateTimeOffset LastTouched { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>The last converged solution and its topology hash, or <see langword="null"/> before the first.</summary>
    public WarmStart? LastSolution { get; private set; }

    /// <summary>Whether a draft solve is registered as running.</summary>
    public bool HasDraftInFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlight is not null;
            }
        }
    }

    /// <summary>Cancels the draft in flight, if any, and registers a new one.</summary>
    /// <param name="requestAborted">The new request's own abort token; the returned source is linked to it.</param>
    /// <returns>The token source the new draft runs under. Hand it back to <see cref="Release"/> when done.</returns>
    public CancellationTokenSource Supersede(CancellationToken requestAborted)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);

        lock (_gate)
        {
            var previous = _inFlight;
            _inFlight = next;
            LastTouched = DateTimeOffset.UtcNow;

            // Cancel under the lock so that a draft cannot register between the swap and the cancel.
            // The previous owner disposes its own source in Release; it is still alive here because
            // Release nulls the field before disposing, and the field no longer names it.
            previous?.Cancel();
        }

        return next;
    }

    /// <summary>Clears a draft's registration and disposes its token source.</summary>
    /// <param name="draft">What <see cref="Supersede"/> returned.</param>
    public void Release(CancellationTokenSource draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        lock (_gate)
        {
            if (ReferenceEquals(_inFlight, draft))
            {
                _inFlight = null;
            }

            draft.Dispose();
        }
    }

    /// <summary>Keeps a solution for the next request to warm-start from.</summary>
    /// <param name="solution">The solution, or <see langword="null"/> to forget the last one.</param>
    public void Remember(WarmStart? solution)
    {
        lock (_gate)
        {
            LastSolution = solution;
            LastTouched = DateTimeOffset.UtcNow;
        }
    }
}
