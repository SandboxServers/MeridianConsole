using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class OrganizationEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations")
            .WithTags("Organizations")
            .RequireAuthorization();

        group.MapGet("", ListOrganizations)
            .WithName("ListOrganizations")
            .WithDescription("List all organizations the current user belongs to");

        group.MapGet("/{organizationId:guid}", GetOrganization)
            .WithName("GetOrganization")
            .WithDescription("Get organization details by ID");

        group.MapPost("", CreateOrganization)
            .WithName("CreateOrganization")
            .WithDescription("Create a new organization");

        group.MapPatch("/{organizationId:guid}", UpdateOrganization)
            .WithName("UpdateOrganization")
            .WithDescription("Update organization details");

        group.MapDelete("/{organizationId:guid}", DeleteOrganization)
            .WithName("DeleteOrganization")
            .WithDescription("Soft-delete an organization");

        group.MapPost("/{organizationId:guid}/switch", SwitchOrganization)
            .WithName("SwitchOrganization")
            .WithDescription("Switch to a different organization and get new tokens")
            .RequireRateLimiting("auth");

        group.MapPost("/{organizationId:guid}/transfer-ownership", TransferOwnership)
            .WithName("TransferOwnership")
            .WithDescription("Transfer organization ownership to another member");
    }

    private static async Task<IResult> ListOrganizations(
        HttpContext context,
        OrganizationService organizationService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var orgs = await organizationService.ListForUserAsync(userId, ct);
        return Results.Ok(orgs);
    }

    private static async Task<IResult> GetOrganization(
        HttpContext context,
        Guid organizationId,
        OrganizationService organizationService,
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
            "org:read",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await organizationService.GetAsync(organizationId, ct);
        return result.Success
            ? Results.Ok(result.Value)
            : ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.OrganizationNotFound, result.Error);
    }

    private static async Task<IResult> CreateOrganization(
        HttpContext context,
        OrganizationCreateRequest request,
        OrganizationService organizationService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await organizationService.CreateAsync(userId, request, ct);
        return result.Success
            ? Results.Created($"/organizations/{result.Value?.Id}", new { id = result.Value?.Id })
            : ProblemDetailsHelper.BadRequest(ErrorCodes.Common.ValidationFailed, result.Error);
    }

    private static async Task<IResult> UpdateOrganization(
        HttpContext context,
        Guid organizationId,
        OrganizationUpdateRequest request,
        OrganizationService organizationService,
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
            "org:write",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await organizationService.UpdateAsync(organizationId, request, ct);
        return result.Success
            ? Results.Ok(result.Value)
            : ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.OrganizationNotFound, result.Error);
    }

    private static async Task<IResult> DeleteOrganization(
        HttpContext context,
        Guid organizationId,
        OrganizationService organizationService,
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
            "org:delete",
            permissionService,
            ct);

        if (permissionResult is not null)
        {
            return permissionResult;
        }

        var result = await organizationService.SoftDeleteAsync(organizationId, ct);
        return result.Success
            ? Results.NoContent()
            : ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.OrganizationNotFound, result.Error);
    }

    private static async Task<IResult> SwitchOrganization(
        HttpContext context,
        Guid organizationId,
        OrganizationSwitchService switchService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var outcome = await switchService.SwitchAsync(userId, organizationId, ct);
        if (!outcome.Success)
        {
            return ProblemDetailsHelper.BadRequest(ErrorCodes.Common.ValidationFailed, outcome.Error);
        }

        return Results.Ok(new
        {
            accessToken = outcome.AccessToken,
            refreshToken = outcome.RefreshToken,
            expiresIn = outcome.ExpiresIn,
            organizationId = outcome.OrganizationId,
            permissions = outcome.Permissions
        });
    }

    private static async Task<IResult> TransferOwnership(
        HttpContext context,
        Guid organizationId,
        TransferOwnershipRequest request,
        OrganizationService organizationService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await organizationService.TransferOwnershipAsync(
            organizationId,
            userId,
            request.NewOwnerId,
            ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                "not_owner" => ProblemDetailsHelper.Forbidden(ErrorCodes.Auth.AccessDenied, "Only the organization owner can transfer ownership."),
                "org_not_found" => ProblemDetailsHelper.NotFound(ErrorCodes.IdentityErrors.OrganizationNotFound),
                _ => ProblemDetailsHelper.BadRequest(ErrorCodes.Common.ValidationFailed, result.Error)
            };
        }

        return Results.Ok(new { message = "Ownership transferred successfully" });
    }
}

public sealed record TransferOwnershipRequest(Guid NewOwnerId);
