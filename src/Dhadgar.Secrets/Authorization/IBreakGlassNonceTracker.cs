namespace Dhadgar.Secrets.Authorization;

/// <summary>
/// Tracks break-glass token nonces to enforce single-use semantics.
/// <para>
/// Production deployments running more than one instance MUST use a durable,
/// strongly-consistent implementation (e.g., Redis- or database-backed): a
/// per-process store cannot detect a nonce consumed on another replica or
/// before a restart.
/// </para>
/// </summary>
public interface IBreakGlassNonceTracker
{
    /// <summary>
    /// Attempts to consume a nonce. Returns true if the nonce was valid and has not been used before.
    /// Returns false if the nonce has already been consumed (replay).
    /// <para>
    /// Contract: <paramref name="nonce"/> must be non-null, non-empty, and not whitespace-only.
    /// Callers are expected to reject tokens with missing or blank nonce claims before
    /// consulting the tracker; a blank value must never be "consumed" (doing so would
    /// permanently replay-reject every subsequent token carrying a blank nonce).
    /// </para>
    /// </summary>
    /// <param name="nonce">The single-use nonce value from the break-glass token. Must not be null, empty, or whitespace.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="nonce"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="nonce"/> is empty or whitespace-only.</exception>
    Task<bool> TryConsumeNonceAsync(string nonce);
}
