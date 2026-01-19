using Dhadgar.Identity.Data;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Endpoints;

/// <summary>
/// Self-service endpoints for the authenticated user.
/// All endpoints operate on the current user's data only.
/// </summary>
public static class MeEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/me")
            .WithTags("Me (Self-Service)")
            .RequireAuthorization();

        group.MapGet("", GetProfile)
            .WithName("GetMyProfile")
            .WithDescription("Get the current user's profile");

        group.MapPatch("", UpdateProfile)
            .WithName("UpdateMyProfile")
            .WithDescription("Update the current user's profile");

        group.MapGet("/organizations", GetOrganizations)
            .WithName("GetMyOrganizations")
            .WithDescription("List organizations the current user belongs to");

        group.MapGet("/linked-accounts", GetLinkedAccounts)
            .WithName("GetMyLinkedAccounts")
            .WithDescription("List OAuth accounts linked to the current user");

        group.MapGet("/permissions", GetPermissions)
            .WithName("GetMyPermissions")
            .WithDescription("Get current user's permissions in their active organization");

        group.MapGet("/invitations", GetPendingInvitations)
            .WithName("GetMyPendingInvitations")
            .WithDescription("List pending organization invitations for the current user");

        group.MapDelete("", RequestAccountDeletion)
            .WithName("RequestAccountDeletion")
            .WithDescription("Request deletion of the current user's account (30-day grace period)");

        group.MapPost("/cancel-deletion", CancelAccountDeletion)
            .WithName("CancelAccountDeletion")
            .WithDescription("Cancel a pending account deletion request");
    }

    private static async Task<IResult> GetProfile(
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.EmailVerified,
                u.PreferredOrganizationId,
                u.HasPasskeysRegistered,
                u.CreatedAt,
                u.LastAuthenticatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return Results.NotFound(new { error = "user_not_found" });
        }

        // Get auth providers (login methods) for this user
        var authProviders = await dbContext.UserLogins
            .AsNoTracking()
            .Where(ul => ul.UserId == userId)
            .Select(ul => new
            {
                Provider = ul.LoginProvider,
                DisplayName = ul.ProviderDisplayName
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.EmailVerified,
            user.PreferredOrganizationId,
            user.HasPasskeysRegistered,
            user.CreatedAt,
            user.LastAuthenticatedAt,
            AuthProviders = authProviders
        });
    }

    private static async Task<IResult> UpdateProfile(
        HttpContext context,
        UpdateProfileRequest request,
        IdentityDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);

        if (user is null)
        {
            return Results.NotFound(new { error = "user_not_found" });
        }

        var updated = false;

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
            updated = true;
        }

        if (request.PreferredOrganizationId.HasValue)
        {
            // Verify user is a member of this organization
            var isMember = await dbContext.UserOrganizations
                .AsNoTracking()
                .AnyAsync(uo =>
                    uo.UserId == userId &&
                    uo.OrganizationId == request.PreferredOrganizationId.Value &&
                    uo.IsActive &&
                    uo.LeftAt == null,
                    ct);

            if (!isMember)
            {
                return Results.BadRequest(new { error = "not_member_of_organization" });
            }

            user.PreferredOrganizationId = request.PreferredOrganizationId.Value;
            updated = true;
        }

        if (!updated)
        {
            return Results.BadRequest(new { error = "no_updates_provided" });
        }

        user.UpdatedAt = timeProvider.GetUtcNow().DateTime;
        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.EmailVerified,
            user.PreferredOrganizationId
        });
    }

    private static async Task<IResult> GetOrganizations(
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var organizations = await dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo =>
                uo.UserId == userId &&
                uo.IsActive &&
                uo.LeftAt == null)
            .Include(uo => uo.Organization)
            .Select(uo => new
            {
                uo.Organization.Id,
                uo.Organization.Name,
                uo.Organization.Slug,
                uo.Role,
                JoinedAt = uo.JoinedAt,
                IsPreferred = uo.OrganizationId == dbContext.Users
                    .Where(u => u.Id == userId)
                    .Select(u => u.PreferredOrganizationId)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return Results.Ok(new { organizations });
    }

    private static async Task<IResult> GetLinkedAccounts(
        HttpContext context,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var linkedAccounts = await dbContext.LinkedAccounts
            .AsNoTracking()
            .Where(la => la.UserId == userId)
            .Select(la => new
            {
                la.Id,
                la.Provider,
                ProviderDisplayName = la.ProviderMetadata != null ? la.ProviderMetadata.DisplayName : null,
                la.LinkedAt,
                la.LastUsedAt
            })
            .ToListAsync(ct);

        return Results.Ok(new { linkedAccounts });
    }

    private static async Task<IResult> GetPermissions(
        HttpContext context,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var orgId = EndpointHelpers.GetOrganizationId(context);
        if (!orgId.HasValue)
        {
            return Results.BadRequest(new { error = "no_organization_context" });
        }

        var permissions = await permissionService.CalculatePermissionsAsync(userId, orgId.Value, ct);

        return Results.Ok(new
        {
            organizationId = orgId.Value,
            permissions = permissions.OrderBy(p => p).ToList()
        });
    }

    private static async Task<IResult> GetPendingInvitations(
        HttpContext context,
        MembershipService membershipService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var invitations = await membershipService.GetPendingInvitationsForUserAsync(userId, ct);
        return Results.Ok(new { invitations });
    }

    private static async Task<IResult> RequestAccountDeletion(
        HttpContext context,
        UserService userService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await userService.RequestDeletionAsync(userId, ct);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        return Results.Ok(new
        {
            message = "Account deletion scheduled",
            scheduledDeletionAt = result.Value,
            gracePeriodDays = 30
        });
    }

    private static async Task<IResult> CancelAccountDeletion(
        HttpContext context,
        UserService userService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await userService.CancelDeletionAsync(userId, ct);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        return Results.Ok(new { message = "Account deletion cancelled" });
    }
}

public sealed record UpdateProfileRequest(
    string? DisplayName,
    Guid? PreferredOrganizationId);
