using System.Security.Claims;

namespace Dhadgar.Secrets.Authorization;

/// <summary>
/// Service for authorizing access to secrets.
/// Supports permission hierarchy, service accounts, and break-glass access.
/// </summary>
public interface ISecretsAuthorizationService
{
    /// <summary>
    /// Checks if the user is authorized to perform the specified action on a secret.
    /// </summary>
    AuthorizationResult Authorize(ClaimsPrincipal user, string secretName, SecretAction action);

    /// <summary>
    /// Checks if the user is authorized to access a category of secrets.
    /// </summary>
    AuthorizationResult AuthorizeCategory(ClaimsPrincipal user, string category, SecretAction action);
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
