using Dhadgar.Identity.Services;
using Dhadgar.Identity.Validators;
using Dhadgar.ServiceDefaults.Pagination;
using Dhadgar.ServiceDefaults.Problems;
using FluentValidation;

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

    private static async Task<IResult?> ValidateSearchParamsAsync(
        string? query,
        int? page,
        int? pageSize,
        IValidator<SearchQueryParameters> validator,
        CancellationToken ct)
    {
        var searchParams = new SearchQueryParameters { Query = query, Page = page, PageSize = pageSize };
        var validationResult = await validator.ValidateAsync(searchParams, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        return null;
    }

    private static async Task<IResult> SearchOrganizations(
        HttpContext context,
        string? query,
        int? page,
        int? pageSize,
        OrganizationService organizationService,
        IValidator<SearchQueryParameters> validator,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var validationError = await ValidateSearchParamsAsync(query, page, pageSize, validator, ct);
        if (validationError is not null)
        {
            return validationError;
        }

        var allResults = await organizationService.SearchForUserAsync(userId, query ?? string.Empty, ct);

        return Results.Ok(allResults.ToPagedResponse(page, pageSize));
    }

    private static async Task<IResult> SearchUsers(
        HttpContext context,
        Guid organizationId,
        string? query,
        int? page,
        int? pageSize,
        UserService userService,
        IPermissionService permissionService,
        IValidator<SearchQueryParameters> validator,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var validationError = await ValidateSearchParamsAsync(query, page, pageSize, validator, ct);
        if (validationError is not null)
        {
            return validationError;
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

        return Results.Ok(allResults.ToPagedResponse(page, pageSize));
    }

    private static async Task<IResult> SearchRoles(
        HttpContext context,
        Guid organizationId,
        string? query,
        int? page,
        int? pageSize,
        RoleService roleService,
        IPermissionService permissionService,
        IValidator<SearchQueryParameters> validator,
        CancellationToken ct)
    {
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var validationError = await ValidateSearchParamsAsync(query, page, pageSize, validator, ct);
        if (validationError is not null)
        {
            return validationError;
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

        return Results.Ok(allResults.ToPagedResponse(page, pageSize));
    }
}
