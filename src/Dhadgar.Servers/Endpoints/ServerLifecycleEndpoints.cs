using Dhadgar.Servers.Services;

namespace Dhadgar.Servers.Endpoints;

public static class ServerLifecycleEndpoints
{
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

        if (!result.Success)
        {
            if (result.Error == "server_not_found")
                return ProblemDetailsHelper.NotFound(result.Error);
            return ProblemDetailsHelper.BadRequest(result.Error ?? "start_failed");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> StopServer(
        Guid organizationId,
        Guid serverId,
        IServerLifecycleService lifecycleService,
        CancellationToken ct = default)
    {
        var result = await lifecycleService.StopServerAsync(organizationId, serverId, ct);

        if (!result.Success)
        {
            if (result.Error == "server_not_found")
                return ProblemDetailsHelper.NotFound(result.Error);
            return ProblemDetailsHelper.BadRequest(result.Error ?? "stop_failed");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> RestartServer(
        Guid organizationId,
        Guid serverId,
        IServerLifecycleService lifecycleService,
        CancellationToken ct = default)
    {
        var result = await lifecycleService.RestartServerAsync(organizationId, serverId, ct);

        if (!result.Success)
        {
            if (result.Error == "server_not_found")
                return ProblemDetailsHelper.NotFound(result.Error);
            return ProblemDetailsHelper.BadRequest(result.Error ?? "restart_failed");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> KillServer(
        Guid organizationId,
        Guid serverId,
        IServerLifecycleService lifecycleService,
        CancellationToken ct = default)
    {
        var result = await lifecycleService.KillServerAsync(organizationId, serverId, ct);

        if (!result.Success)
        {
            if (result.Error == "server_not_found")
                return ProblemDetailsHelper.NotFound(result.Error);
            return ProblemDetailsHelper.BadRequest(result.Error ?? "kill_failed");
        }

        return Results.NoContent();
    }
}
