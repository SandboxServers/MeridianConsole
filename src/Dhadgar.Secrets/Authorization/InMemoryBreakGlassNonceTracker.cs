using System.Collections.Concurrent;

namespace Dhadgar.Secrets.Authorization;

/// <summary>
/// In-memory break-glass nonce tracker with automatic expiry cleanup.
/// For multi-instance deployments, replace with a distributed implementation (e.g., Redis-backed).
/// </summary>
public sealed class InMemoryBreakGlassNonceTracker : IBreakGlassNonceTracker, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumedNonces = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromHours(2);

    public InMemoryBreakGlassNonceTracker()
    {
        _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public Task<bool> TryConsumeNonceAsync(string nonce)
    {
        var consumed = _consumedNonces.TryAdd(nonce, DateTimeOffset.UtcNow);
        return Task.FromResult(consumed);
    }

    private void Cleanup(object? state)
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

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
