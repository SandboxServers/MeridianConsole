using Dhadgar.Servers.Services;
using Dhadgar.Shared.Results;

namespace Dhadgar.Servers.Endpoints;

public static class ServerLifecycleEndpoints
{
    private static IResult HandleLifecycleResult(Result<bool> result, string defaultError)
    {
        if (!result.IsSuccess)
        {
            return result.Error == "server_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error ?? defaultError);
        }

        return Results.NoContent();
    }

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/servers/{serverId:guid}")
            .WithTags("Server Lifecycle")
            .RequireAuthorization("TenantScoped");

        group.MapPost("/start", StartServer)
            .WithName("StartServer")
            .WithDescription("Start a server")
            .WithSummary("Start server")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapPost("/stop", StopServer)
            .WithName("StopServer")
            .WithDescription("Stop a server gracefully")
            .WithSummary("Stop server")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapPost("/restart", RestartServer)
            .WithName("RestartServer")
            .WithDescription("Restart a server")
            .WithSummary("Restart server")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapPost("/kill", KillServer)
            .WithName("KillServer")
            .WithDescription("Force-kill a server")
            .WithSummary("Kill server")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);
    }

    private static async Task<IResult> StartServer(
        Guid organizationId,
        Guid serverId,
        IServerLifecycleService lifecycleService,
        CancellationToken ct = default)
    {
        var result = await lifecycleService.StartServerAsync(organizationId, serverId, ct);
        return HandleLifecycleResult(result, "start_failed");
    }

    private static async Task<IResult> StopServer(
        Guid organizationId,
        Guid serverId,
        IServerLifecycleService lifecycleService,
        CancellationToken ct = default)
    {
        var result = await lifecycleService.StopServerAsync(organizationId, serverId, ct);
        return HandleLifecycleResult(result, "stop_failed");
    }

    private static async Task<IResult> RestartServer(
        Guid organizationId,
        Guid serverId,
        IServerLifecycleService lifecycleService,
        CancellationToken ct = default)
    {
        var result = await lifecycleService.RestartServerAsync(organizationId, serverId, ct);
        return HandleLifecycleResult(result, "restart_failed");
    }

    private static async Task<IResult> KillServer(
        Guid organizationId,
        Guid serverId,
        IServerLifecycleService lifecycleService,
        CancellationToken ct = default)
    {
        var result = await lifecycleService.KillServerAsync(organizationId, serverId, ct);
        return HandleLifecycleResult(result, "kill_failed");
    }
}
