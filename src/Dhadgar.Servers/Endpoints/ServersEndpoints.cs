using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Services;

namespace Dhadgar.Servers.Endpoints;

public static class ServersEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/servers")
            .WithTags("Servers")
            .RequireAuthorization("TenantScoped");

        group.MapGet("", ListServers)
            .WithName("ListServers")
            .WithDescription("List all servers for an organization with filtering, sorting, and pagination.")
            .WithSummary("List servers")
            .Produces<Dhadgar.Contracts.FilteredPagedResponse<ServerListItem>>();

        group.MapGet("/{serverId:guid}", GetServer)
            .WithName("GetServer")
            .WithDescription("Get server details by ID")
            .WithSummary("Get server")
            .Produces<ServerDetail>()
            .ProducesProblem(404);

        group.MapPost("", CreateServer)
            .WithName("CreateServer")
            .WithDescription("Create a new server")
            .WithSummary("Create server")
            .Produces<ServerDetail>(201)
            .ProducesProblem(400);

        group.MapPatch("/{serverId:guid}", UpdateServer)
            .WithName("UpdateServer")
            .WithDescription("Update server properties")
            .WithSummary("Update server")
            .Produces<ServerDetail>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapDelete("/{serverId:guid}", DeleteServer)
            .WithName("DeleteServer")
            .WithDescription("Delete a server (soft delete)")
            .WithSummary("Delete server")
            .Produces(204)
            .ProducesProblem(404);
    }

    private static async Task<IResult> ListServers(
        Guid organizationId,
        IServerService serverService,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        string? powerState = null,
        string? gameType = null,
        Guid? nodeId = null,
        string? search = null,
        string? tags = null,
        string sortBy = "name",
        string sortOrder = "asc",
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var query = new ServerListQuery(
            page, pageSize, status, powerState, gameType, nodeId,
            search, tags, sortBy, sortOrder, includeDeleted);

        var result = await serverService.GetServersAsync(organizationId, query, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetServer(
        Guid organizationId,
        Guid serverId,
        IServerService serverService,
        CancellationToken ct = default)
    {
        var result = await serverService.GetServerAsync(organizationId, serverId, ct);

        if (!result.Success)
        {
            return ProblemDetailsHelper.NotFound(result.Error ?? "server_not_found");
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateServer(
        Guid organizationId,
        CreateServerRequest request,
        IServerService serverService,
        CancellationToken ct = default)
    {
        var result = await serverService.CreateServerAsync(organizationId, request, ct);

        if (!result.Success)
        {
            return ProblemDetailsHelper.BadRequest(result.Error ?? "create_failed");
        }

        return Results.Created($"/organizations/{organizationId}/servers/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> UpdateServer(
        Guid organizationId,
        Guid serverId,
        UpdateServerRequest request,
        IServerService serverService,
        CancellationToken ct = default)
    {
        var result = await serverService.UpdateServerAsync(organizationId, serverId, request, ct);

        if (!result.Success)
        {
            return result.Error == "server_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error ?? "update_failed");
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DeleteServer(
        Guid organizationId,
        Guid serverId,
        IServerService serverService,
        CancellationToken ct = default)
    {
        var result = await serverService.DeleteServerAsync(organizationId, serverId, ct);

        if (!result.Success)
        {
            return ProblemDetailsHelper.NotFound(result.Error ?? "server_not_found");
        }

        return Results.NoContent();
    }
}
