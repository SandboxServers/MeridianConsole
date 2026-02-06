using Dhadgar.Console.Services;
using Dhadgar.Contracts.Console;
using Dhadgar.ServiceDefaults.Problems;
using FluentValidation;

namespace Dhadgar.Console.Endpoints;

public static class ConsoleEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/servers/{serverId:guid}/console")
            .WithTags("Console")
            .RequireAuthorization("TenantScoped");

        group.MapGet("/history", GetHistory)
            .WithName("GetConsoleHistory")
            .WithDescription("Get recent console history for a server")
            .WithSummary("Get console history")
            .Produces<ConsoleHistoryDto>();

        group.MapPost("/search", SearchHistory)
            .WithName("SearchConsoleHistory")
            .WithDescription("Search console history")
            .WithSummary("Search console history")
            .Produces<ConsoleHistorySearchResult>();
    }

    private static async Task<IResult> GetHistory(
        Guid organizationId,
        Guid serverId,
        IConsoleHistoryService historyService,
        HttpContext httpContext,
        int lineCount = 100,
        CancellationToken ct = default)
    {
        // Validate tenant access
        var userOrgId = httpContext.User?.FindFirst("org_id")?.Value;
        if (!Guid.TryParse(userOrgId, out var claimOrgId) || claimOrgId != organizationId)
        {
            return Results.Forbid();
        }

        // Clamp lineCount to valid range
        lineCount = Math.Clamp(lineCount, 1, 1000);

        var lines = await historyService.GetRecentHistoryAsync(serverId, lineCount, ct);

        var result = new ConsoleHistoryDto(
            serverId,
            lines,
            lines.Count >= lineCount,
            lines.Count > 0 ? lines[^1].Timestamp : null);

        return Results.Ok(result);
    }

    private static async Task<IResult> SearchHistory(
        Guid organizationId,
        Guid serverId,
        SearchConsoleHistoryRequest request,
        IConsoleHistoryService historyService,
        IValidator<SearchConsoleHistoryRequest> validator,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        // Validate tenant access
        var userOrgId = httpContext.User?.FindFirst("org_id")?.Value;
        if (!Guid.TryParse(userOrgId, out var claimOrgId) || claimOrgId != organizationId)
        {
            return Results.Forbid();
        }

        // Clamp pagination to valid ranges
        var clampedPage = Math.Max(1, request.Page);
        var clampedPageSize = Math.Clamp(request.PageSize, 1, 100);

        // Ensure the request has the correct server ID and clamped pagination
        var searchRequest = request with
        {
            ServerId = serverId,
            Page = clampedPage,
            PageSize = clampedPageSize
        };
        var result = await historyService.SearchHistoryAsync(organizationId, searchRequest, ct);
        return Results.Ok(result);
    }
}
