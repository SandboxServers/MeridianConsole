using Dhadgar.Contracts;
using Dhadgar.Nodes.Models;

namespace Dhadgar.Nodes.Services;

public interface INodeService
{
    /// <summary>
    /// Gets a paginated list of nodes with optional filtering and sorting.
    /// </summary>
    Task<FilteredPagedResponse<NodeListItem>> GetNodesAsync(
        Guid organizationId,
        NodeListQuery query,
        CancellationToken ct = default);

    /// <summary>
    /// Gets detailed information about a specific node.
    /// </summary>
    Task<ServiceResult<NodeDetail>> GetNodeAsync(
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a node's properties (name, displayName).
    /// </summary>
    Task<ServiceResult<NodeDetail>> UpdateNodeAsync(
        Guid nodeId,
        UpdateNodeRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a node's tags.
    /// </summary>
    Task<ServiceResult<NodeDetail>> UpdateNodeTagsAsync(
        Guid nodeId,
        UpdateNodeTagsRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Decommissions a node (soft delete).
    /// </summary>
    Task<ServiceResult<bool>> DecommissionNodeAsync(
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Puts a node into maintenance mode.
    /// </summary>
    Task<ServiceResult<bool>> EnterMaintenanceAsync(
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Takes a node out of maintenance mode.
    /// </summary>
    Task<ServiceResult<bool>> ExitMaintenanceAsync(
        Guid nodeId,
        CancellationToken ct = default);
}
