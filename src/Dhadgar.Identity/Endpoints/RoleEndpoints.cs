using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class RoleEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/roles")
            .WithTags("Roles");

        group.MapGet("", ListRoles)
            .WithName("ListRoles")
            .WithDescription("List all roles available in the organization");

        group.MapGet("/{roleId}", GetRole)
            .WithName("GetRole")
            .WithDescription("Get role details by ID");

        group.MapPost("", CreateRole)
            .WithName("CreateRole")
            .WithDescription("Create a custom role in the organization");

        group.MapPatch("/{roleId}", UpdateRole)
            .WithName("UpdateRole")
            .WithDescription("Update a custom role");

        group.MapDelete("/{roleId}", DeleteRole)
            .WithName("DeleteRole")
            .WithDescription("Delete a custom role");

        group.MapPost("/{roleId}/assign", AssignRole)
            .WithName("AssignRole")
            .WithDescription("Assign a role to a user")
            .RequireRateLimiting("Auth");

        group.MapPost("/{roleId}/revoke", RevokeRole)
            .WithName("RevokeRole")
            .WithDescription("Revoke a role from a user")
            .RequireRateLimiting("Auth");

        group.MapGet("/{roleId}/members", GetRoleMembers)
            .WithName("GetRoleMembers")
            .WithDescription("List all members with a specific role");
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

        // Pass actor userId for privilege escalation validation
        var result = await roleService.CreateAsync(organizationId, userId, request, ct);
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

    private static async Task<IResult> UpdateRole(
        HttpContext context,
        Guid organizationId,
        string roleId,
        RoleUpdateRequest request,
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

        var result = await roleService.UpdateAsync(organizationId, userId, roleId, request, ct);
        return result.Success ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> DeleteRole(
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
            "members:roles",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await roleService.DeleteAsync(organizationId, userId, roleId, ct);
        return result.Success ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> GetRoleMembers(
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

        var result = await roleService.GetMembersAsync(organizationId, roleId, ct);
        return result.Success ? Results.Ok(result.Value) : Results.NotFound(new { error = result.Error });
    }
}
