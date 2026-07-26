using System.Security.Claims;

namespace Dhadgar.Secrets.Authorization;

/// <summary>
/// Centralized break-glass policy constants shared by the authorization service
/// and the nonce tracker, so their timing invariants cannot drift apart.
/// </summary>
public static class BreakGlassPolicy
{
    /// <summary>
    /// Maximum time-to-live a break-glass token may declare via <c>break_glass_exp</c>.
    /// </summary>
    public static readonly TimeSpan MaxTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// How long consumed nonces must be retained for replay detection.
    /// Derived from <see cref="MaxTtl"/> (2x) so that a nonce is never evicted
    /// while a token carrying it could still be within its validity window.
    /// </summary>
    public static readonly TimeSpan NonceRetentionPeriod = MaxTtl * 2;
}

/// <summary>
/// Service for authorizing access to secrets.
/// Supports permission hierarchy, service accounts, and break-glass access.
/// </summary>
public interface ISecretsAuthorizationService
{
    /// <summary>
    /// Checks if the user is authorized to perform the specified action on a secret.
    /// Asynchronous because break-glass validation consumes a single-use nonce via
    /// <see cref="IBreakGlassNonceTracker"/>, which may be backed by a distributed store.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, string secretName, SecretAction action);

    /// <summary>
    /// Checks if the user is authorized to access a category of secrets.
    /// Asynchronous because break-glass validation consumes a single-use nonce via
    /// <see cref="IBreakGlassNonceTracker"/>, which may be backed by a distributed store.
    /// </summary>
    Task<AuthorizationResult> AuthorizeCategoryAsync(ClaimsPrincipal user, string category, SecretAction action);
}

public enum SecretAction
{
    Read,
    Write,
    Rotate,
    Delete,
    List
}

public sealed record AuthorizationResult
{
    public bool IsAuthorized { get; init; }
    public string? DenialReason { get; init; }
    public bool IsBreakGlass { get; init; }
    public bool IsServiceAccount { get; init; }
    public string? PrincipalType { get; init; }
    public string? UserId { get; init; }

    public static AuthorizationResult Success(string? userId = null, string? principalType = null, bool isBreakGlass = false, bool isServiceAccount = false)
        => new()
        {
            IsAuthorized = true,
            UserId = userId,
            PrincipalType = principalType,
            IsBreakGlass = isBreakGlass,
            IsServiceAccount = isServiceAccount
        };

    public static AuthorizationResult Denied(string reason, string? userId = null)
        => new()
        {
            IsAuthorized = false,
            DenialReason = reason,
            UserId = userId
        };
}
