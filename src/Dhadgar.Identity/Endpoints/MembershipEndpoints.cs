using Dhadgar.Contracts;
using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class MembershipEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/organizations/{organizationId:guid}/members", ListMembers);
        app.MapPost("/organizations/{organizationId:guid}/members/invite", InviteMember)
            .RequireRateLimiting("invite");
        app.MapPost("/organizations/{organizationId:guid}/members/accept", AcceptInvite);
        app.MapPost("/organizations/{organizationId:guid}/members/reject", RejectInvite);
        app.MapDelete("/organizations/{organizationId:guid}/invitations/{targetUserId:guid}", WithdrawInvitation);
        app.MapDelete("/organizations/{organizationId:guid}/members/{memberId:guid}", RemoveMember);
        app.MapPost("/organizations/{organizationId:guid}/members/{memberId:guid}/role", AssignRole);
        app.MapPost("/organizations/{organizationId:guid}/members/{memberId:guid}/claims", AddClaim);
        app.MapDelete("/organizations/{organizationId:guid}/members/{memberId:guid}/claims/{claimId:guid}", RemoveClaim);
        app.MapPost("/organizations/{organizationId:guid}/members/bulk-invite", BulkInviteMembers)
            .RequireRateLimiting("invite");
        app.MapPost("/organizations/{organizationId:guid}/members/bulk-remove", BulkRemoveMembers);
    }

    private static async Task<IResult> ListMembers(
        HttpContext context,
        Guid organizationId,
        MembershipService membershipService,
        IPermissionService permissionService,
        int? page,
        int? limit,
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

        var pagination = new PaginationRequest { Page = page ?? 1, Limit = limit ?? 50 };
        var allMembers = await membershipService.ListMembersAsync(organizationId, ct);

        // Apply pagination in memory (for backward compatibility with existing service)
        var pagedMembers = allMembers
            .Skip(pagination.Skip)
            .Take(pagination.NormalizedLimit)
            .ToArray();

        return Results.Ok(PagedResponse<MemberSummary>.Create(pagedMembers, allMembers.Count, pagination));
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

    private static async Task<IResult> RejectInvite(
        HttpContext context,
        Guid organizationId,
        MembershipService membershipService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await membershipService.RejectInviteAsync(organizationId, userId, ct);
        return result.Success
            ? Results.NoContent()
            : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> WithdrawInvitation(
        HttpContext context,
        Guid organizationId,
        Guid targetUserId,
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

        var result = await membershipService.WithdrawInviteAsync(organizationId, userId, targetUserId, ct);
        return result.Success
            ? Results.NoContent()
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

        var role = request.Role?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(role))
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

    private static async Task<IResult> BulkInviteMembers(
        HttpContext context,
        Guid organizationId,
        BulkInviteRequest request,
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

        if (request.Invites is null || request.Invites.Count == 0)
        {
            return Results.BadRequest(new { error = "no_invites_provided" });
        }

        var result = await membershipService.BulkInviteAsync(organizationId, userId, request.Invites, ct);

        return Results.Ok(new
        {
            succeeded = result.Succeeded,
            failed = result.Failed.Select(e => new { itemId = e.ItemId, error = e.ErrorCode, details = e.Details }),
            totalRequested = result.TotalRequested,
            successCount = result.Succeeded.Count,
            failCount = result.Failed.Count
        });
    }

    private static async Task<IResult> BulkRemoveMembers(
        HttpContext context,
        Guid organizationId,
        BulkRemoveRequest request,
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

        if (request.MemberIds is null || request.MemberIds.Count == 0)
        {
            return Results.BadRequest(new { error = "no_members_provided" });
        }

        var result = await membershipService.BulkRemoveAsync(organizationId, request.MemberIds, ct);

        return Results.Ok(new
        {
            succeeded = result.Succeeded,
            failed = result.Failed.Select(e => new { itemId = e.ItemId, error = e.ErrorCode, details = e.Details }),
            totalRequested = result.TotalRequested,
            successCount = result.Succeeded.Count,
            failCount = result.Failed.Count
        });
    }
}

public sealed record BulkInviteRequest(IReadOnlyCollection<MemberInviteRequest> Invites);
public sealed record BulkRemoveRequest(IReadOnlyCollection<Guid> MemberIds);
