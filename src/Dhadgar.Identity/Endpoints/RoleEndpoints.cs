using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class RoleEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/organizations/{organizationId:guid}/roles", ListRoles);
        app.MapGet("/organizations/{organizationId:guid}/roles/{roleId}", GetRole);
        app.MapPost("/organizations/{organizationId:guid}/roles", CreateRole);
        app.MapPost("/organizations/{organizationId:guid}/roles/{roleId}/assign", AssignRole);
        app.MapPost("/organizations/{organizationId:guid}/roles/{roleId}/revoke", RevokeRole);
    }

    private static async Task<IResult> ListRoles(
        HttpContext context,
        Guid organizationId,
        RoleService roleService,
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

        var roles = await roleService.ListAsync(organizationId, ct);
        return Results.Ok(roles);
    }

    private static async Task<IResult> GetRole(
        HttpContext context,
        Guid organizationId,
        string roleId,
        RoleService roleService,
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

        var result = await roleService.GetAsync(organizationId, roleId, ct);
        return result.Success ? Results.Ok(result.Value) : Results.NotFound(new { error = result.Error });
    }

    private static async Task<IResult> CreateRole(
        HttpContext context,
        Guid organizationId,
        RoleCreateRequest request,
        RoleService roleService,
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
            "members:roles",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await roleService.CreateAsync(organizationId, request, ct);
        return result.Success
            ? Results.Created($"/organizations/{organizationId}/roles/{result.Value?.Id}", result.Value)
            : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> AssignRole(
        HttpContext context,
        Guid organizationId,
        string roleId,
        RoleAssignmentRequest request,
        RoleService roleService,
        MembershipService membershipService,
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
            "members:roles",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await roleService.AssignRoleAsync(
            organizationId,
            actorUserId,
            request.UserId,
            roleId,
            membershipService,
            ct);

        return result.Success ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> RevokeRole(
        HttpContext context,
        Guid organizationId,
        string roleId,
        RoleAssignmentRequest request,
        RoleService roleService,
        MembershipService membershipService,
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
            "members:roles",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await roleService.RevokeRoleAsync(
            organizationId,
            actorUserId,
            request.UserId,
            roleId,
            membershipService,
            ct);

        return result.Success ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
    }
}
