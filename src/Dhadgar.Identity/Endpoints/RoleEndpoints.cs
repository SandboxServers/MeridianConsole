using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class RoleEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/roles")
            .WithTags("Roles")
            .RequireAuthorization();

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
        return result.Success
            ? Results.Ok(result.Value)
            : ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.RoleNotFound, result.Error);
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
        if (result.Success)
        {
            return Results.Created($"/organizations/{organizationId}/roles/{result.Value?.Id}", result.Value);
        }

        // Map specific error codes to appropriate HTTP status and error codes
        return result.Error switch
        {
            "role_already_exists" => ProblemDetailsHelper.Conflict(ErrorCodes.IdentityErrors.RoleAlreadyExists),
            "role_name_required" or "role_name_too_long" or "reserved_role_name" =>
                ProblemDetailsHelper.BadRequest(ErrorCodes.IdentityErrors.InvalidRoleName, result.Error),
            "unknown_permissions" or "cannot_grant_unowned_permissions" =>
                ProblemDetailsHelper.BadRequest(ErrorCodes.IdentityErrors.InvalidPermissions, result.Error),
            "org_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.OrganizationNotFound),
            _ => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error)
        };
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

        if (result.Success)
        {
            return Results.Ok(result.Value);
        }

        return result.Error switch
        {
            "role_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.RoleNotFound, result.Error),
            "user_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.UserNotFound, result.Error),
            "member_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.MemberNotFound, result.Error),
            "cannot_assign_role_with_unowned_permissions" =>
                ProblemDetailsHelper.Forbidden(ErrorCodes.IdentityErrors.InvalidPermissions, result.Error),
            "role_not_assigned" => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error),
            _ => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error)
        };
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

        if (result.Success)
        {
            return Results.Ok(result.Value);
        }

        return result.Error switch
        {
            "role_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.RoleNotFound, result.Error),
            "user_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.UserNotFound, result.Error),
            "member_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.MemberNotFound, result.Error),
            "cannot_assign_role_with_unowned_permissions" =>
                ProblemDetailsHelper.Forbidden(ErrorCodes.IdentityErrors.InvalidPermissions, result.Error),
            "role_not_assigned" => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error),
            _ => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error)
        };
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
        if (result.Success)
        {
            return Results.Ok(result.Value);
        }

        return result.Error switch
        {
            "role_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.RoleNotFound, result.Error),
            "cannot_update_system_role" => ProblemDetailsHelper.Forbidden(ErrorCodes.CommonErrors.ValidationFailed, result.Error),
            "role_name_too_long" => ProblemDetailsHelper.BadRequest(ErrorCodes.IdentityErrors.InvalidRoleName, result.Error),
            _ => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error)
        };
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
        if (result.Success)
        {
            return Results.NoContent();
        }

        return result.Error switch
        {
            "role_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.RoleNotFound, result.Error),
            "cannot_delete_system_role" => ProblemDetailsHelper.Forbidden(ErrorCodes.CommonErrors.ValidationFailed, result.Error),
            "role_has_active_members" => ProblemDetailsHelper.Conflict(ErrorCodes.CommonErrors.ValidationFailed, result.Error),
            _ => ProblemDetailsHelper.BadRequest(ErrorCodes.CommonErrors.ValidationFailed, result.Error)
        };
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
        return result.Success
            ? Results.Ok(result.Value)
            : ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.RoleNotFound, result.Error);
    }
}
