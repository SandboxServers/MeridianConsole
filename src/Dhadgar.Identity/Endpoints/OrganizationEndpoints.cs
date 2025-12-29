using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class OrganizationEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/organizations", ListOrganizations);
        app.MapGet("/organizations/{organizationId:guid}", GetOrganization);
        app.MapPost("/organizations", CreateOrganization);
        app.MapPatch("/organizations/{organizationId:guid}", UpdateOrganization);
        app.MapDelete("/organizations/{organizationId:guid}", DeleteOrganization);
        app.MapPost("/organizations/{organizationId:guid}/switch", SwitchOrganization);
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
        return result.Success ? Results.Ok(result.Value) : Results.NotFound(new { error = result.Error });
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
            : Results.BadRequest(new { error = result.Error });
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
        return result.Success ? Results.Ok(result.Value) : Results.NotFound(new { error = result.Error });
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
        return result.Success ? Results.NoContent() : Results.NotFound(new { error = result.Error });
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
            return Results.BadRequest(new { error = outcome.Error });
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
}
