using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;

namespace Dhadgar.Nodes.Endpoints;

public static class NodesEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/organizations/{organizationId:guid}/nodes")
            .WithTags("Nodes")
            .RequireAuthorization("TenantScoped");

        group.MapGet("", ListNodes)
            .WithName("ListNodes")
            .WithDescription("List all nodes for an organization with filtering, sorting, and pagination. " +
                "Supports filtering by status, platform, health score range, active servers, tags, and full-text search.")
            .WithSummary("List nodes")
            .Produces<FilteredPagedResponse<NodeListItem>>();

        group.MapGet("/{nodeId:guid}", GetNode)
            .WithName("GetNode")
            .WithDescription("Get node details by ID")
            .WithSummary("Get node")
            .Produces<NodeDetail>()
            .ProducesProblem(404);

        group.MapPatch("/{nodeId:guid}", UpdateNode)
            .WithName("UpdateNode")
            .WithDescription("Update node properties (name, displayName)")
            .WithSummary("Update node")
            .Produces<NodeDetail>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapPut("/{nodeId:guid}/tags", UpdateNodeTags)
            .WithName("UpdateNodeTags")
            .WithDescription("Update node tags (replaces all existing tags)")
            .WithSummary("Update node tags")
            .Produces<NodeDetail>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapDelete("/{nodeId:guid}", DecommissionNode)
            .WithName("DecommissionNode")
            .WithDescription("Decommission a node (soft delete)")
            .WithSummary("Decommission node")
            .Produces(204)
            .ProducesProblem(404);

        group.MapPost("/{nodeId:guid}/maintenance", EnterMaintenance)
            .WithName("EnterMaintenance")
            .WithDescription("Put node into maintenance mode")
            .WithSummary("Enter maintenance mode")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);

        group.MapDelete("/{nodeId:guid}/maintenance", ExitMaintenance)
            .WithName("ExitMaintenance")
            .WithDescription("Take node out of maintenance mode")
            .WithSummary("Exit maintenance mode")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);
    }

    private static async Task<IResult> ListNodes(
        Guid organizationId,
        INodeService nodeService,
        int page = 1,
        int pageSize = 20,
        NodeStatus? status = null,
        string? platform = null,
        int? minHealthScore = null,
        int? maxHealthScore = null,
        bool? hasActiveServers = null,
        string? search = null,
        string? tags = null,
        string sortBy = "name",
        string sortOrder = "asc",
        bool includeDecommissioned = false,
        CancellationToken ct = default)
    {
        var query = new NodeListQuery
        {
            Page = page,
            PageSize = pageSize,
            Status = status,
            Platform = platform,
            MinHealthScore = minHealthScore,
            MaxHealthScore = maxHealthScore,
            HasActiveServers = hasActiveServers,
            Search = search,
            Tags = tags,
            SortBy = sortBy,
            SortOrder = sortOrder,
            IncludeDecommissioned = includeDecommissioned
        };
        var result = await nodeService.GetNodesAsync(organizationId, query, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetNode(
        Guid organizationId,
        Guid nodeId,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        var result = await nodeService.GetNodeAsync(organizationId, nodeId, ct);

        if (!result.Success)
        {
            return ProblemDetailsHelper.NotFound(result.Error ?? "node_not_found");
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> UpdateNode(
        Guid organizationId,
        Guid nodeId,
        UpdateNodeRequest request,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        var result = await nodeService.UpdateNodeAsync(organizationId, nodeId, request, ct);

        if (!result.Success)
        {
            return result.Error == "node_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error ?? "update_failed");
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> UpdateNodeTags(
        Guid organizationId,
        Guid nodeId,
        UpdateNodeTagsRequest request,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        var result = await nodeService.UpdateNodeTagsAsync(organizationId, nodeId, request, ct);

        if (!result.Success)
        {
            return result.Error == "node_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error ?? "update_failed");
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DecommissionNode(
        Guid organizationId,
        Guid nodeId,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        var result = await nodeService.DecommissionNodeAsync(organizationId, nodeId, ct);

        if (!result.Success)
        {
            return result.Error == "node_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error ?? "decommission_failed");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> EnterMaintenance(
        Guid organizationId,
        Guid nodeId,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        var result = await nodeService.EnterMaintenanceAsync(organizationId, nodeId, ct);

        if (!result.Success)
        {
            return result.Error == "node_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error ?? "maintenance_failed");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ExitMaintenance(
        Guid organizationId,
        Guid nodeId,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        var result = await nodeService.ExitMaintenanceAsync(organizationId, nodeId, ct);

        if (!result.Success)
        {
            return result.Error == "node_not_found"
                ? ProblemDetailsHelper.NotFound(result.Error)
                : ProblemDetailsHelper.BadRequest(result.Error ?? "maintenance_failed");
        }

        return Results.NoContent();
    }
}
