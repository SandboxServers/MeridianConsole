using System.Security.Claims;
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Extensions;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Endpoints;

public static class EndpointHelpers
{
    /// <summary>
    /// Extracts user ID from authenticated JWT claims only.
    /// SECURITY: This method does NOT trust headers - only validated JWT claims.
    /// </summary>
    public static bool TryGetUserId(HttpContext context, out Guid userId)
    {
        // SECURITY FIX: Only trust authenticated JWT claims, never headers
        // The X-User-Id header was a security vulnerability that allowed impersonation
        var id = context.User.GetUserId();
        if (id.HasValue)
        {
            userId = id.Value;
            return true;
        }

        userId = Guid.Empty;
        return false;
    }

    /// <summary>
    /// Extracts user ID from ClaimsPrincipal (for use in endpoint handlers that receive ClaimsPrincipal directly).
    /// </summary>
    public static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var id = user.GetUserId();
        if (id.HasValue)
        {
            userId = id.Value;
            return true;
        }

        userId = Guid.Empty;
        return false;
    }

    /// <summary>
    /// Extracts organization ID from authenticated JWT claims.
    /// </summary>
    public static bool TryGetOrganizationId(HttpContext context, out Guid organizationId)
    {
        var id = context.User.GetOrganizationId();
        if (id.HasValue)
        {
            organizationId = id.Value;
            return true;
        }

        organizationId = Guid.Empty;
        return false;
    }

    /// <summary>
    /// Gets organization ID from authenticated JWT claims, or null if not present.
    /// </summary>
    public static Guid? GetOrganizationId(HttpContext context)
    {
        return context.User.GetOrganizationId();
    }

    public static async Task<IResult?> RequirePermissionAsync(
        Guid userId,
        Guid organizationId,
        string permission,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var permissions = await permissionService.CalculatePermissionsAsync(userId, organizationId, ct);
        return permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)
            ? null
            : Results.Forbid();
    }

    /// <summary>
    /// Verifies that the user's email is verified if the organization requires it.
    /// Returns null if verification passes (or is not required), otherwise returns a 403 result.
    /// </summary>
    public static async Task<IResult?> RequireVerifiedEmailAsync(
        HttpContext context,
        Guid organizationId,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        // Check if org requires email verification
        var org = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Id == organizationId && o.DeletedAt == null)
            .Select(o => new { o.Settings.RequireEmailVerification })
            .FirstOrDefaultAsync(ct);

        if (org is null)
        {
            return ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.OrganizationNotFound);
        }

        if (!org.RequireEmailVerification)
        {
            return null; // Organization doesn't require email verification
        }

        // Check user's email verification status from JWT claims
        if (context.User.IsEmailVerified())
        {
            return null; // Email is verified
        }

        return ProblemDetailsHelper.Forbidden(ErrorCodes.Auth.AccessDenied, "This organization requires email verification.");
    }

    /// <summary>
    /// Checks if the current user's email is verified based on JWT claims.
    /// Does not check organization requirements - use RequireVerifiedEmailAsync for that.
    /// </summary>
    public static bool IsEmailVerified(HttpContext context)
    {
        return context.User.IsEmailVerified();
    }
}
