using System.Collections.Concurrent;

namespace Dhadgar.Secrets.Authorization;

/// <summary>
/// In-memory break-glass nonce tracker with automatic expiry cleanup.
/// <para>
/// Suitable for single-instance deployments only: nonces are per-process, so
/// replicas and restarts defeat the single-use guarantee. For multi-instance
/// deployments, replace with a distributed implementation (e.g., Redis-backed).
/// </para>
/// </summary>
public sealed class InMemoryBreakGlassNonceTracker : IBreakGlassNonceTracker, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumedNonces = new();
    private readonly Timer _cleanupTimer;
    private readonly ILogger<InMemoryBreakGlassNonceTracker>? _logger;

    // Derived from the break-glass max TTL so a nonce can never be evicted while a
    // token carrying it is still within its validity window.
    private readonly TimeSpan _retentionPeriod = BreakGlassPolicy.NonceRetentionPeriod;

    public InMemoryBreakGlassNonceTracker(ILogger<InMemoryBreakGlassNonceTracker>? logger = null)
    {
        _logger = logger;
        _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    /// <inheritdoc />
    public Task<bool> TryConsumeNonceAsync(string nonce)
    {
        // Enforce the interface contract explicitly: a blank nonce must never be
        // "consumed" (that would permanently replay-reject all blank-nonce tokens),
        // and null would otherwise surface as an opaque ConcurrentDictionary error.
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        var consumed = _consumedNonces.TryAdd(nonce, DateTimeOffset.UtcNow);
        return Task.FromResult(consumed);
    }

    private void Cleanup(object? state)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow - _retentionPeriod;
            foreach (var kvp in _consumedNonces)
            {
                if (kvp.Value < cutoff)
                {
                    _consumedNonces.TryRemove(kvp.Key, out _);
                }
            }
        }
        catch (Exception ex)
        {
            // Never let an exception escape the timer callback: it would silently
            // kill the cleanup timer and nonce retention would grow unbounded.
            _logger?.LogError(ex, "Break-glass nonce cleanup failed; will retry on next timer tick.");
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
