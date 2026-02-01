using Dhadgar.Console.Services;
using Dhadgar.Contracts.Console;

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
        int lineCount = 100,
        CancellationToken ct = default)
    {
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
        CancellationToken ct = default)
    {
        // Ensure the request has the correct server ID
        var searchRequest = request with { ServerId = serverId };
        var result = await historyService.SearchHistoryAsync(searchRequest, ct);
        return Results.Ok(result);
    }
}
