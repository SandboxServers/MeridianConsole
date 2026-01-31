using Dhadgar.Contracts;
using Dhadgar.Identity.Services;

namespace Dhadgar.Identity.Endpoints;

public static class SearchEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/organizations/search", SearchOrganizations)
            .WithTags("Search")
            .WithName("SearchOrganizations")
            .WithDescription("Search organizations the current user belongs to")
            .RequireAuthorization();

        app.MapGet("/organizations/{organizationId:guid}/users/search", SearchUsers)
            .WithTags("Search")
            .WithName("SearchUsers")
            .WithDescription("Search users within an organization")
            .RequireAuthorization();

        app.MapGet("/organizations/{organizationId:guid}/roles/search", SearchRoles)
            .WithTags("Search")
            .WithName("SearchRoles")
            .WithDescription("Search roles within an organization")
            .RequireAuthorization();
    }

    private static async Task<IResult> SearchOrganizations(
        HttpContext context,
        string? query,
        int? page,
        int? pageSize,
        OrganizationService organizationService,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var allResults = await organizationService.SearchForUserAsync(userId, query ?? string.Empty, ct);

        // Apply pagination
        var pagination = new PaginationRequest { Page = page ?? 1, PageSize = pageSize ?? 50 };
        var pagedResults = allResults
            .Skip(pagination.Skip)
            .Take(pagination.NormalizedPageSize)
            .ToArray();

        return Results.Ok(PagedResponse<OrganizationSummary>.Create(pagedResults, allResults.Count, pagination));
    }

    private static async Task<IResult> SearchUsers(
        HttpContext context,
        Guid organizationId,
        string? query,
        int? page,
        int? pageSize,
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

        var allResults = await userService.SearchAsync(organizationId, query ?? string.Empty, ct);

        // Apply pagination
        var pagination = new PaginationRequest { Page = page ?? 1, PageSize = pageSize ?? 50 };
        var pagedResults = allResults
            .Skip(pagination.Skip)
            .Take(pagination.NormalizedPageSize)
            .ToArray();

        return Results.Ok(PagedResponse<UserSummary>.Create(pagedResults, allResults.Count, pagination));
    }

    private static async Task<IResult> SearchRoles(
        HttpContext context,
        Guid organizationId,
        string? query,
        int? page,
        int? pageSize,
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

        var allResults = await roleService.SearchAsync(organizationId, query ?? string.Empty, ct);

        // Apply pagination
        var pagination = new PaginationRequest { Page = page ?? 1, PageSize = pageSize ?? 50 };
        var pagedResults = allResults
            .Skip(pagination.Skip)
            .Take(pagination.NormalizedPageSize)
            .ToArray();

        return Results.Ok(PagedResponse<RoleSummary>.Create(pagedResults, allResults.Count, pagination));
    }
}
