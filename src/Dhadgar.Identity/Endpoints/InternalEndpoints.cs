using Dhadgar.Identity.Data;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Endpoints;

/// <summary>
/// Internal endpoints for service-to-service communication.
/// These endpoints are meant to be called by other microservices, not directly by clients.
/// They should be protected by service authentication (client credentials flow) in production.
/// </summary>
public static class InternalEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/internal")
            .WithTags("Internal (Service-to-Service)");

        // User lookups
        group.MapGet("/users/{userId:guid}", GetUser)
            .WithName("InternalGetUser")
            .WithDescription("Get user info by ID (service-to-service)");

        group.MapPost("/users/batch", GetUsersBatch)
            .WithName("InternalGetUsersBatch")
            .WithDescription("Get multiple users by IDs (service-to-service)");

        // Organization lookups
        group.MapGet("/organizations/{organizationId:guid}", GetOrganization)
            .WithName("InternalGetOrganization")
            .WithDescription("Get organization info (service-to-service)");

        group.MapGet("/organizations/{organizationId:guid}/exists", CheckOrganizationExists)
            .WithName("InternalCheckOrganizationExists")
            .WithDescription("Check if organization exists (lightweight)");

        group.MapGet("/organizations/{organizationId:guid}/members", GetOrganizationMembers)
            .WithName("InternalGetOrganizationMembers")
            .WithDescription("Get organization members (service-to-service)");

        // Permission checks
        group.MapPost("/permissions/check", CheckPermission)
            .WithName("InternalCheckPermission")
            .WithDescription("Check if user has permission in organization");

        group.MapGet("/users/{userId:guid}/organizations/{organizationId:guid}/permissions", GetUserPermissions)
            .WithName("InternalGetUserPermissions")
            .WithDescription("Get user's permissions in organization");

        // Membership validation
        group.MapGet("/users/{userId:guid}/organizations/{organizationId:guid}/membership", GetMembership)
            .WithName("InternalGetMembership")
            .WithDescription("Get user's membership in organization");
    }

    private static async Task<IResult> GetUser(
        Guid userId,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new UserInfo(
                u.Id,
                u.Email ?? string.Empty,
                u.DisplayName,
                true)) // Active if not deleted
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return Results.NotFound(new { error = "user_not_found" });
        }

        return Results.Ok(user);
    }

    private static async Task<IResult> GetUsersBatch(
        UserBatchRequest request,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        if (request.UserIds is null || request.UserIds.Count == 0)
        {
            return Results.BadRequest(new { error = "no_user_ids_provided" });
        }

        if (request.UserIds.Count > 100)
        {
            return Results.BadRequest(new { error = "too_many_user_ids", maxAllowed = 100 });
        }

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(u => request.UserIds.Contains(u.Id) && u.DeletedAt == null)
            .Select(u => new UserInfo(
                u.Id,
                u.Email ?? string.Empty,
                u.DisplayName,
                true))
            .ToListAsync(ct);

        var result = users.ToDictionary(u => u.Id);
        return Results.Ok(new UserBatchResponse(result));
    }

    private static async Task<IResult> GetOrganization(
        Guid organizationId,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        var org = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Id == organizationId && o.DeletedAt == null)
            .Select(o => new OrganizationInfo(
                o.Id,
                o.Name,
                o.Slug,
                o.OwnerId,
                true)) // Active if not deleted
            .FirstOrDefaultAsync(ct);

        if (org is null)
        {
            return Results.NotFound(new { error = "organization_not_found" });
        }

        return Results.Ok(org);
    }

    private static async Task<IResult> CheckOrganizationExists(
        Guid organizationId,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        var exists = await dbContext.Organizations
            .AsNoTracking()
            .AnyAsync(o => o.Id == organizationId && o.DeletedAt == null, ct);

        return Results.Ok(new { exists });
    }

    private static async Task<IResult> GetOrganizationMembers(
        Guid organizationId,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        var members = await dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo =>
                uo.OrganizationId == organizationId &&
                uo.IsActive &&
                uo.LeftAt == null)
            .Select(uo => new OrganizationMemberInfo(
                uo.UserId,
                uo.Role ?? "member",
                uo.IsActive))
            .ToListAsync(ct);

        return Results.Ok(new { members, count = members.Count });
    }

    private static async Task<IResult> CheckPermission(
        PermissionCheckRequest request,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var permissions = await permissionService.CalculatePermissionsAsync(
            request.UserId,
            request.OrganizationId,
            ct);

        var hasPermission = permissions.Contains(request.Permission, StringComparer.OrdinalIgnoreCase);

        return Results.Ok(new PermissionCheckResponse(hasPermission));
    }

    private static async Task<IResult> GetUserPermissions(
        Guid userId,
        Guid organizationId,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var permissions = await permissionService.CalculatePermissionsAsync(userId, organizationId, ct);

        return Results.Ok(new
        {
            userId,
            organizationId,
            permissions = permissions.OrderBy(p => p).ToList()
        });
    }

    private static async Task<IResult> GetMembership(
        Guid userId,
        Guid organizationId,
        IdentityDbContext dbContext,
        CancellationToken ct)
    {
        var membership = await dbContext.UserOrganizations
            .AsNoTracking()
            .Where(uo =>
                uo.UserId == userId &&
                uo.OrganizationId == organizationId &&
                uo.LeftAt == null)
            .Select(uo => new
            {
                uo.UserId,
                uo.OrganizationId,
                uo.Role,
                uo.IsActive,
                uo.JoinedAt
            })
            .FirstOrDefaultAsync(ct);

        if (membership is null)
        {
            return Results.NotFound(new { error = "membership_not_found" });
        }

        return Results.Ok(membership);
    }
}

// DTOs for internal service communication
public sealed record UserInfo(Guid Id, string Email, string? DisplayName, bool IsActive);
public sealed record UserBatchRequest(IReadOnlyCollection<Guid> UserIds);
public sealed record UserBatchResponse(Dictionary<Guid, UserInfo> Users);
public sealed record OrganizationInfo(Guid Id, string Name, string Slug, Guid OwnerId, bool IsActive);
public sealed record OrganizationMemberInfo(Guid UserId, string Role, bool IsActive);
public sealed record PermissionCheckRequest(Guid UserId, Guid OrganizationId, string Permission);
public sealed record PermissionCheckResponse(bool HasPermission);
