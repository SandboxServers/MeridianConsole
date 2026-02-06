using Dhadgar.Contracts;
using Dhadgar.Contracts.Mods;
using Dhadgar.Mods.Services;
using Dhadgar.ServiceDefaults.Problems;
using FluentValidation;

namespace Dhadgar.Mods.Endpoints;

public static class ModsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/mods")
            .WithTags("Mods")
            .RequireAuthorization("TenantScoped");

        group.MapGet("", ListMods)
            .WithName("ListMods")
            .WithDescription("List mods for an organization with optional filtering")
            .WithSummary("List mods")
            .Produces<FilteredPagedResponse<ModListItem>>();

        group.MapGet("/{modId:guid}", GetMod)
            .WithName("GetMod")
            .WithDescription("Get mod details by ID")
            .WithSummary("Get mod")
            .Produces<ModDetail>()
            .ProducesProblem(404);

        group.MapPost("", CreateMod)
            .WithName("CreateMod")
            .WithDescription("Create a new mod")
            .WithSummary("Create mod")
            .Produces<ModDetail>(201)
            .ProducesProblem(400);

        group.MapPatch("/{modId:guid}", UpdateMod)
            .WithName("UpdateMod")
            .WithDescription("Update mod properties")
            .WithSummary("Update mod")
            .Produces<ModDetail>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapDelete("/{modId:guid}", DeleteMod)
            .WithName("DeleteMod")
            .WithDescription("Delete a mod (soft delete)")
            .WithSummary("Delete mod")
            .Produces(204)
            .ProducesProblem(404);

        // Public mods discovery
        var publicGroup = app.MapGroup("/mods/public")
            .WithTags("Mod Discovery");

        publicGroup.MapGet("", ListPublicMods)
            .WithName("ListPublicMods")
            .WithDescription("List public mods for discovery")
            .WithSummary("Discover public mods")
            .Produces<FilteredPagedResponse<ModListItem>>();

        publicGroup.MapGet("/{modId:guid}", GetPublicMod)
            .WithName("GetPublicMod")
            .WithDescription("Get public mod details")
            .WithSummary("Get public mod")
            .Produces<ModDetail>()
            .ProducesProblem(404);
    }

    private static async Task<IResult> ListMods(
        Guid organizationId,
        IModService modService,
        string? query = null,
        string? gameType = null,
        Guid? categoryId = null,
        string? tags = null,
        string sortBy = "downloads",
        string sortOrder = "desc",
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var searchQuery = new ModSearchQuery(
            query, gameType, categoryId, tags, null, sortBy, sortOrder, page, pageSize);

        var result = await modService.GetModsAsync(organizationId, searchQuery, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListPublicMods(
        IModService modService,
        string? query = null,
        string? gameType = null,
        Guid? categoryId = null,
        string? tags = null,
        string sortBy = "downloads",
        string sortOrder = "desc",
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var searchQuery = new ModSearchQuery(
            query, gameType, categoryId, tags, true, sortBy, sortOrder, page, pageSize);

        var result = await modService.GetModsAsync(null, searchQuery, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMod(
        Guid organizationId,
        Guid modId,
        IModService modService,
        CancellationToken ct = default)
    {
        var result = await modService.GetModAsync(modId, organizationId, ct);

        if (!result.IsSuccess)
        {
            return ProblemDetailsHelper.NotFound(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetPublicMod(
        Guid modId,
        IModService modService,
        CancellationToken ct = default)
    {
        var result = await modService.GetModAsync(modId, null, ct);

        if (!result.IsSuccess)
        {
            return ProblemDetailsHelper.NotFound(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateMod(
        Guid organizationId,
        CreateModRequest request,
        IModService modService,
        IValidator<CreateModRequest> validator,
        CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await modService.CreateModAsync(organizationId, request, ct);

        if (!result.IsSuccess)
        {
            return result.Error == "mod_slug_exists"
                ? ProblemDetailsHelper.Conflict(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error);
        }

        return Results.Created($"/organizations/{organizationId}/mods/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> UpdateMod(
        Guid organizationId,
        Guid modId,
        UpdateModRequest request,
        IModService modService,
        IValidator<UpdateModRequest> validator,
        CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await modService.UpdateModAsync(organizationId, modId, request, ct);

        if (!result.IsSuccess)
        {
            return result.Error == "mod_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DeleteMod(
        Guid organizationId,
        Guid modId,
        IModService modService,
        CancellationToken ct = default)
    {
        var result = await modService.DeleteModAsync(organizationId, modId, ct);

        if (!result.IsSuccess)
        {
            return ProblemDetailsHelper.NotFound(result.Error);
        }

        return Results.NoContent();
    }
}
