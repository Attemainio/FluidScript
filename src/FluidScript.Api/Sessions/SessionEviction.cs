using Microsoft.Extensions.Options;

namespace FluidScript.Api.Sessions;

/// <summary>Drops idle sessions on a timer, so a client that went away stops costing memory.</summary>
/// <param name="sessions">The store.</param>
/// <param name="options">The idle timeout and the interval.</param>
/// <param name="logger">Where evictions are counted.</param>
public sealed partial class SessionEviction(ISessionStore sessions, IOptions<ApiOptions> options, ILogger<SessionEviction> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SessionEvictionInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var dropped = sessions.Evict(options.Value.SessionIdleTimeout);

                if (dropped > 0)
                {
                    LogEvicted(logger, dropped, sessions.Count);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping; there is nothing to finish.
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Evicted {Dropped} idle session(s); {Remaining} remain.")]
    private static partial void LogEvicted(ILogger logger, int dropped, int remaining);
}
