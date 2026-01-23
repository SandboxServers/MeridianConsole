using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dhadgar.Nodes.Endpoints;

public static class NodesEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/nodes")
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
        [AsParameters] NodeListQuery query,
        CancellationToken ct = default)
    {
        var result = await nodeService.GetNodesAsync(organizationId, query, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetNode(
        Guid organizationId,
        Guid nodeId,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        var result = await nodeService.GetNodeAsync(nodeId, ct);

        if (!result.Success)
        {
            return ProblemDetailsHelper.NotFound(result.Error ?? "node_not_found");
        }

        // Verify organization ownership
        if (result.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
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
        // First verify the node belongs to this organization
        var existing = await nodeService.GetNodeAsync(nodeId, ct);
        if (!existing.Success || existing.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        var result = await nodeService.UpdateNodeAsync(nodeId, request, ct);
        return result.Success
            ? Results.Ok(result.Value)
            : ProblemDetailsHelper.BadRequest(result.Error ?? "update_failed");
    }

    private static async Task<IResult> UpdateNodeTags(
        Guid organizationId,
        Guid nodeId,
        UpdateNodeTagsRequest request,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        // First verify the node belongs to this organization
        var existing = await nodeService.GetNodeAsync(nodeId, ct);
        if (!existing.Success || existing.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        var result = await nodeService.UpdateNodeTagsAsync(nodeId, request, ct);
        return result.Success
            ? Results.Ok(result.Value)
            : ProblemDetailsHelper.BadRequest(result.Error ?? "update_failed");
    }

    private static async Task<IResult> DecommissionNode(
        Guid organizationId,
        Guid nodeId,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        // First verify the node belongs to this organization
        var existing = await nodeService.GetNodeAsync(nodeId, ct);
        if (!existing.Success || existing.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        var result = await nodeService.DecommissionNodeAsync(nodeId, ct);
        return result.Success
            ? Results.NoContent()
            : ProblemDetailsHelper.BadRequest(result.Error ?? "decommission_failed");
    }

    private static async Task<IResult> EnterMaintenance(
        Guid organizationId,
        Guid nodeId,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        // First verify the node belongs to this organization
        var existing = await nodeService.GetNodeAsync(nodeId, ct);
        if (!existing.Success || existing.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        var result = await nodeService.EnterMaintenanceAsync(nodeId, ct);
        return result.Success
            ? Results.NoContent()
            : ProblemDetailsHelper.BadRequest(result.Error ?? "maintenance_failed");
    }

    private static async Task<IResult> ExitMaintenance(
        Guid organizationId,
        Guid nodeId,
        INodeService nodeService,
        CancellationToken ct = default)
    {
        // First verify the node belongs to this organization
        var existing = await nodeService.GetNodeAsync(nodeId, ct);
        if (!existing.Success || existing.Value?.OrganizationId != organizationId)
        {
            return ProblemDetailsHelper.NotFound("node_not_found");
        }

        var result = await nodeService.ExitMaintenanceAsync(nodeId, ct);
        return result.Success
            ? Results.NoContent()
            : ProblemDetailsHelper.BadRequest(result.Error ?? "maintenance_failed");
    }
}
