using Dhadgar.Identity.Data;
using Dhadgar.Identity.Services;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Endpoints;

public static class UserEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/organizations/{organizationId:guid}/users", ListUsers);
        app.MapGet("/organizations/{organizationId:guid}/users/{userId:guid}", GetUser);
        app.MapPost("/organizations/{organizationId:guid}/users", CreateUser);
        app.MapPatch("/organizations/{organizationId:guid}/users/{userId:guid}", UpdateUser);
        app.MapDelete("/organizations/{organizationId:guid}/users/{userId:guid}", DeleteUser);
        app.MapDelete("/organizations/{organizationId:guid}/users/{userId:guid}/linked-accounts/{linkedAccountId:guid}", UnlinkAccount);
    }

    private static async Task<IResult> ListUsers(
        HttpContext context,
        Guid organizationId,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            userId,
            organizationId,
            "members:read",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var users = await userService.ListForOrganizationAsync(organizationId, ct);
        return Results.Ok(users);
    }

    private static async Task<IResult> GetUser(
        HttpContext context,
        Guid organizationId,
        Guid userId,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            actorUserId,
            organizationId,
            "members:read",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await userService.GetAsync(organizationId, userId, ct);
        return result.Success ? Results.Ok(result.Value) : Results.NotFound(new { error = result.Error });
    }

    private static async Task<IResult> CreateUser(
        HttpContext context,
        Guid organizationId,
        UserCreateRequest request,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            actorUserId,
            organizationId,
            "members:invite",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await userService.CreateAsync(organizationId, actorUserId, request, ct);
        return result.Success
            ? Results.Created($"/organizations/{organizationId}/users/{result.Value?.Id}", result.Value)
            : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> UpdateUser(
        HttpContext context,
        Guid organizationId,
        Guid userId,
        UserUpdateRequest request,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            actorUserId,
            organizationId,
            "members:invite",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Results.BadRequest(new { error = "no_updates" });
        }

        var result = await userService.UpdateAsync(organizationId, userId, request, ct);
        return result.Success ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> DeleteUser(
        HttpContext context,
        Guid organizationId,
        Guid userId,
        UserService userService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        var permissionResult = await EndpointHelpers.RequirePermissionAsync(
            actorUserId,
            organizationId,
            "members:remove",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await userService.SoftDeleteAsync(organizationId, userId, ct);
        return result.Success ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> UnlinkAccount(
        HttpContext context,
        Guid organizationId,
        Guid userId,
        Guid linkedAccountId,
        IdentityDbContext dbContext,
        ILinkedAccountService linkedAccountService,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var actorUserId))
        {
            return Results.Unauthorized();
        }

        if (actorUserId != userId)
        {
            var permissionResult = await EndpointHelpers.RequirePermissionAsync(
                actorUserId,
                organizationId,
                "members:roles",
                permissionService,
                ct);

            if (permissionResult is not null)
            {
                return permissionResult;
            }
        }

        var membershipExists = await dbContext.UserOrganizations
            .AsNoTracking()
            .AnyAsync(uo =>
                uo.OrganizationId == organizationId &&
                uo.UserId == userId &&
                uo.LeftAt == null,
                ct);

        if (!membershipExists)
        {
            return Results.NotFound(new { error = "user_not_found" });
        }

        var result = await linkedAccountService.UnlinkAsync(userId, linkedAccountId, ct);
        return result.Success ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
    }
}
