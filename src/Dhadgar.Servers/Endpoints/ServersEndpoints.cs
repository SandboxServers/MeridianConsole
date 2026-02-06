using Dhadgar.Contracts.Servers;
using Dhadgar.Servers.Services;
using Dhadgar.ServiceDefaults.Problems;
using FluentValidation;

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
        HttpContext httpContext,
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
        // Only admins can include soft-deleted servers
        if (includeDeleted && !httpContext.User.IsInRole("admin"))
        {
            includeDeleted = false;
        }

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

        if (!result.IsSuccess)
        {
            return ProblemDetailsHelper.NotFound(result.Error);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateServer(
        Guid organizationId,
        CreateServerRequest request,
        IServerService serverService,
        IValidator<CreateServerRequest> validator,
        CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await serverService.CreateServerAsync(organizationId, request, ct);

        if (!result.IsSuccess)
        {
            return result.Error == "server_name_exists"
                ? ProblemDetailsHelper.Conflict(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error);
        }

        return Results.Created($"/organizations/{organizationId}/servers/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> UpdateServer(
        Guid organizationId,
        Guid serverId,
        UpdateServerRequest request,
        IServerService serverService,
        IValidator<UpdateServerRequest> validator,
        CancellationToken ct = default)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return ProblemDetailsHelper.BadRequest(
                ErrorCodes.CommonErrors.ValidationFailed,
                string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var result = await serverService.UpdateServerAsync(organizationId, serverId, request, ct);

        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                "server_not_found" => ProblemDetailsHelper.NotFound(result.Error),
                "server_name_exists" => ProblemDetailsHelper.Conflict(result.Error),
                _ => ProblemDetailsHelper.BadRequest(result.Error)
            };
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

        if (!result.IsSuccess)
        {
            return ProblemDetailsHelper.NotFound(result.Error);
        }

        return Results.NoContent();
    }
}
