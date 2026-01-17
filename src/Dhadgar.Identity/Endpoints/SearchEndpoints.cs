using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class SearchEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/organizations/search", SearchOrganizations)
            .WithTags("Search")
            .WithName("SearchOrganizations")
            .WithDescription("Search organizations the current user belongs to");

        app.MapGet("/organizations/{organizationId:guid}/users/search", SearchUsers)
            .WithTags("Search")
            .WithName("SearchUsers")
            .WithDescription("Search users within an organization");

        app.MapGet("/organizations/{organizationId:guid}/roles/search", SearchRoles)
            .WithTags("Search")
            .WithName("SearchRoles")
            .WithDescription("Search roles within an organization");
    }

    private static async Task<IResult> SearchOrganizations(
        HttpContext context,
        string? query,
        OrganizationService organizationService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var results = await organizationService.SearchForUserAsync(userId, query ?? string.Empty, ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> SearchUsers(
        HttpContext context,
        Guid organizationId,
        string? query,
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

        var results = await userService.SearchAsync(organizationId, query ?? string.Empty, ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> SearchRoles(
        HttpContext context,
        Guid organizationId,
        string? query,
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

        var results = await roleService.SearchAsync(organizationId, query ?? string.Empty, ct);
        return Results.Ok(results);
    }
}
