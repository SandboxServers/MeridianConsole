namespace Dhadgar.Secrets.Authorization;

/// <summary>
/// Tracks break-glass token nonces to enforce single-use semantics.
/// </summary>
public interface IBreakGlassNonceTracker
{
    /// <summary>
    /// Attempts to consume a nonce. Returns true if the nonce was valid and has not been used before.
    /// Returns false if the nonce has already been consumed (replay).
    /// </summary>
    Task<bool> TryConsumeNonceAsync(string nonce);
}
