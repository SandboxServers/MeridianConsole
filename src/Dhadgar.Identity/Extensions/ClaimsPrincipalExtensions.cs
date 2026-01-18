using System.Security.Claims;

namespace Dhadgar.Identity.Extensions;

/// <summary>
/// Extension methods for extracting user identity from validated JWT claims.
/// These methods only trust authenticated JWT claims, not headers.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extracts the user ID from the authenticated user's JWT claims.
    /// Returns null if the user is not authenticated or the claim is missing/invalid.
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claim = principal.FindFirst(ClaimTypes.NameIdentifier)
                 ?? principal.FindFirst("sub");

        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
    }

    /// <summary>
    /// Extracts the organization ID from the authenticated user's JWT claims.
    /// Returns null if the user is not authenticated or the claim is missing/invalid.
    /// </summary>
    public static Guid? GetOrganizationId(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claim = principal.FindFirst("org_id");
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
    }

    /// <summary>
    /// Gets the user's email from JWT claims.
    /// </summary>
    public static string? GetEmail(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value;
    }

    /// <summary>
    /// Gets the user's role from JWT claims.
    /// </summary>
    public static string? GetRole(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return principal.FindFirst(ClaimTypes.Role)?.Value
            ?? principal.FindFirst("role")?.Value;
    }

    /// <summary>
    /// Gets all permissions from JWT claims.
    /// </summary>
    public static IReadOnlyList<string> GetPermissions(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Array.Empty<string>();
        }

        return principal.FindAll("permission")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    /// <summary>
    /// Checks if the user has a specific permission in their JWT claims.
    /// </summary>
    public static bool HasPermission(this ClaimsPrincipal? principal, string permission)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return principal.FindAll("permission")
            .Any(c => string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the JWT token ID (jti claim) for token revocation purposes.
    /// </summary>
    public static string? GetTokenId(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return principal.FindFirst("jti")?.Value;
    }

    /// <summary>
    /// Gets the client type from JWT claims (e.g., "agent", "user", "service").
    /// </summary>
    public static string? GetClientType(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return principal.FindFirst("client_type")?.Value;
    }

    /// <summary>
    /// Checks if the user's email has been verified.
    /// </summary>
    public static bool IsEmailVerified(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var claim = principal.FindFirst("email_verified")?.Value;
        return string.Equals(claim, "true", StringComparison.OrdinalIgnoreCase);
    }
}
