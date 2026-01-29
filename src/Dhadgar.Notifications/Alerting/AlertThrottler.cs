using System.Collections.Concurrent;

namespace Dhadgar.Notifications.Alerting;

/// <summary>
/// Throttles alerts to prevent alert storms. Uses a sliding window per alert key.
/// </summary>
public sealed class AlertThrottler
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAlertTimes = new();
    private readonly TimeSpan _throttleWindow;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a new throttler with the specified throttle window.
    /// </summary>
    /// <param name="throttleWindow">Minimum time between duplicate alerts.</param>
    /// <param name="timeProvider">Optional time provider for testing. Defaults to system time.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when throttleWindow is zero or negative.</exception>
    public AlertThrottler(TimeSpan throttleWindow, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(throttleWindow, TimeSpan.Zero, nameof(throttleWindow));
        _throttleWindow = throttleWindow;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Checks if an alert should be sent or throttled.
    /// </summary>
    /// <param name="alert">The alert to check.</param>
    /// <returns>True if alert should be sent; false if throttled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when alert is null.</exception>
    public bool ShouldSend(AlertMessage alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var key = GetAlertKey(alert);
        var now = _timeProvider.GetUtcNow();
        var shouldSend = false;

        // Use atomic AddOrUpdate to prevent race conditions
        _lastAlertTimes.AddOrUpdate(
            key,
            addValueFactory: _ =>
            {
                shouldSend = true;
                return now;
            },
            updateValueFactory: (_, lastTime) =>
            {
                if (now - lastTime >= _throttleWindow)
                {
                    shouldSend = true;
                    return now;
                }
                // Throttled - keep the existing time
                shouldSend = false;
                return lastTime;
            });

        if (shouldSend)
        {
            CleanupOldEntries(now);
        }

        return shouldSend;
    }

    private static string GetAlertKey(AlertMessage alert)
    {
        // Key on service + title + exception type for deduplication
        return $"{alert.ServiceName}:{alert.Title}:{alert.ExceptionType ?? "none"}";
    }

    private void CleanupOldEntries(DateTimeOffset now)
    {
        // Remove entries older than 2x throttle window to prevent memory growth
        var cutoff = now - (_throttleWindow * 2);
        var keysToRemove = _lastAlertTimes
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _lastAlertTimes.TryRemove(key, out _);
        }
    }
}
