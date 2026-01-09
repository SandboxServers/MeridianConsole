using Dhadgar.Identity.Authorization;
using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class MembershipEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/organizations/{organizationId:guid}/members", ListMembers);
        app.MapPost("/organizations/{organizationId:guid}/members/invite", InviteMember);
        app.MapPost("/organizations/{organizationId:guid}/members/accept", AcceptInvite);
        app.MapDelete("/organizations/{organizationId:guid}/members/{memberId:guid}", RemoveMember);
        app.MapPost("/organizations/{organizationId:guid}/members/{memberId:guid}/role", AssignRole);
        app.MapPost("/organizations/{organizationId:guid}/members/{memberId:guid}/claims", AddClaim);
        app.MapDelete("/organizations/{organizationId:guid}/members/{memberId:guid}/claims/{claimId:guid}", RemoveClaim);
    }

    private static async Task<IResult> ListMembers(
        HttpContext context,
        Guid organizationId,
        MembershipService membershipService,
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

        var members = await membershipService.ListMembersAsync(organizationId, ct);
        return Results.Ok(members);
    }

    private static async Task<IResult> InviteMember(
        HttpContext context,
        Guid organizationId,
        MemberInviteRequest request,
        MembershipService membershipService,
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
            "members:invite",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await membershipService.InviteAsync(organizationId, userId, request, ct);
        return result.Success
            ? Results.Ok(new { membershipId = result.Value?.Id })
            : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> AcceptInvite(
        HttpContext context,
        Guid organizationId,
        MembershipService membershipService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await membershipService.AcceptInviteAsync(organizationId, userId, ct);
        return result.Success
            ? Results.Ok(new { membershipId = result.Value?.Id })
            : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> RemoveMember(
        HttpContext context,
        Guid organizationId,
        Guid memberId,
        MembershipService membershipService,
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
            "members:remove",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await membershipService.RemoveMemberAsync(organizationId, memberId, ct);
        return result.Success ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> AssignRole(
        HttpContext context,
        Guid organizationId,
        Guid memberId,
        MemberRoleRequest request,
        MembershipService membershipService,
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

        var role = request.Role?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!RoleDefinitions.IsValidRole(role))
        {
            return Results.BadRequest(new { error = "invalid_role" });
        }

        var result = await membershipService.AssignRoleAsync(organizationId, userId, memberId, role, ct);
        return result.Success ? Results.Ok(new { role = result.Value?.Role }) : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> AddClaim(
        HttpContext context,
        Guid organizationId,
        Guid memberId,
        MemberClaimRequest request,
        MembershipService membershipService,
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

        var result = await membershipService.AddClaimAsync(organizationId, userId, memberId, request, ct);
        return result.Success
            ? Results.Ok(new { claimId = result.Value?.Id })
            : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> RemoveClaim(
        HttpContext context,
        Guid organizationId,
        Guid memberId,
        Guid claimId,
        MembershipService membershipService,
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

        var result = await membershipService.RemoveClaimAsync(organizationId, memberId, claimId, ct);
        return result.Success ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
    }
}
