using System.Collections.Concurrent;

namespace Dhadgar.Notifications.Alerting;

/// <summary>
/// Throttles alerts to prevent alert storms. Uses a sliding window per alert key.
/// </summary>
public sealed class AlertThrottler
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAlertTimes = new();
    private readonly TimeSpan _throttleWindow;

    /// <summary>
    /// Creates a new throttler with the specified throttle window.
    /// </summary>
    /// <param name="throttleWindow">Minimum time between duplicate alerts.</param>
    public AlertThrottler(TimeSpan throttleWindow)
    {
        _throttleWindow = throttleWindow;
    }

    /// <summary>
    /// Checks if an alert should be sent or throttled.
    /// </summary>
    /// <param name="alert">The alert to check.</param>
    /// <returns>True if alert should be sent; false if throttled.</returns>
    public bool ShouldSend(AlertMessage alert)
    {
        var key = GetAlertKey(alert);
        var now = DateTimeOffset.UtcNow;

        if (_lastAlertTimes.TryGetValue(key, out var lastTime))
        {
            if (now - lastTime < _throttleWindow)
            {
                return false; // Throttled
            }
        }

        _lastAlertTimes[key] = now;
        CleanupOldEntries(now);
        return true;
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
