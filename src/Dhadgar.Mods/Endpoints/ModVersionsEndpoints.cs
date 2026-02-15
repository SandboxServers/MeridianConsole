using Dhadgar.Contracts.Mods;
using Dhadgar.Mods.Services;
using Dhadgar.ServiceDefaults.Problems;
using FluentValidation;

namespace Dhadgar.Mods.Endpoints;

public static class ModVersionsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/mods/{modId:guid}/versions")
            .WithTags("Mod Versions")
            .RequireAuthorization("TenantScoped");

        group.MapGet("", ListVersions)
            .WithName("ListModVersions")
            .WithDescription("List all versions of a mod")
            .WithSummary("List versions")
            .Produces<IReadOnlyList<ModVersionSummary>>();

        group.MapGet("/latest", GetLatestVersion)
            .WithName("GetLatestModVersion")
            .WithDescription("Get the latest version of a mod")
            .WithSummary("Get latest version")
            .Produces<ModVersionDetail>()
            .ProducesProblem(404);

        group.MapGet("/{versionId:guid}", GetVersion)
            .WithName("GetModVersion")
            .WithDescription("Get a specific version of a mod")
            .WithSummary("Get version")
            .Produces<ModVersionDetail>()
            .ProducesProblem(404);

        group.MapPost("", PublishVersion)
            .WithName("PublishModVersion")
            .WithDescription("Publish a new version of a mod")
            .WithSummary("Publish version")
            .Produces<ModVersionDetail>(201)
            .ProducesProblem(400)
            .ProducesProblem(409);

        group.MapPost("/{versionId:guid}/deprecate", DeprecateVersion)
            .WithName("DeprecateModVersion")
            .WithDescription("Deprecate a version")
            .WithSummary("Deprecate version")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);
    }

    private static async Task<IResult> ListVersions(
        Guid organizationId,
        Guid modId,
        IModVersionService versionService,
        string? constraint = null,
        CancellationToken ct = default)
    {
        var result = await versionService.FindVersionsMatchingAsync(organizationId, modId, constraint, ct);

        if (!result.IsSuccess)
        {
            return ProblemDetailsHelper.NotFound(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetLatestVersion(
        Guid organizationId,
        Guid modId,
        IModVersionService versionService,
        bool includePrerelease = false,
        CancellationToken ct = default)
    {
        var result = await versionService.GetLatestVersionAsync(organizationId, modId, includePrerelease, ct);

        if (!result.IsSuccess)
        {
            return ProblemDetailsHelper.NotFound(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetVersion(
        Guid organizationId,
        Guid modId,
        Guid versionId,
        IModVersionService versionService,
        CancellationToken ct = default)
    {
        var result = await versionService.GetVersionAsync(organizationId, modId, versionId, ct);

        if (!result.IsSuccess)
        {
            return ProblemDetailsHelper.NotFound(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> PublishVersion(
        Guid organizationId,
        Guid modId,
        PublishVersionRequest request,
        IModVersionService versionService,
        IValidator<PublishVersionRequest> validator,
        CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await versionService.PublishVersionAsync(organizationId, modId, request, ct);

        if (!result.IsSuccess)
        {
            return result.Error == "version_already_exists"
                ? ProblemDetailsHelper.Conflict(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error);
        }

        return Results.Created(
            $"/organizations/{organizationId}/mods/{modId}/versions/{result.Value.Id}",
            result.Value);
    }

    private static async Task<IResult> DeprecateVersion(
        Guid organizationId,
        Guid modId,
        Guid versionId,
        DeprecateVersionRequest request,
        IModVersionService versionService,
        IValidator<DeprecateVersionRequest> validator,
        CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await versionService.DeprecateVersionAsync(
            organizationId, modId, versionId, request, ct);

        if (!result.IsSuccess)
        {
            return result.Error == "version_not_found" || result.Error == "mod_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error);
        }

        return Results.NoContent();
    }
}
