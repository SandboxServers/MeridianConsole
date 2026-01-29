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
    /// Organization ownership is verified internally.
    /// </summary>
    Task<ServiceResult<NodeDetail>> GetNodeAsync(
        Guid organizationId,
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a node's properties (name, displayName).
    /// Organization ownership is verified internally.
    /// </summary>
    Task<ServiceResult<NodeDetail>> UpdateNodeAsync(
        Guid organizationId,
        Guid nodeId,
        UpdateNodeRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a node's tags.
    /// Organization ownership is verified internally.
    /// </summary>
    Task<ServiceResult<NodeDetail>> UpdateNodeTagsAsync(
        Guid organizationId,
        Guid nodeId,
        UpdateNodeTagsRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Decommissions a node (soft delete).
    /// Organization ownership is verified internally.
    /// Idempotent: returns success if node is already decommissioned.
    /// </summary>
    Task<ServiceResult<bool>> DecommissionNodeAsync(
        Guid organizationId,
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Puts a node into maintenance mode.
    /// Organization ownership is verified internally.
    /// </summary>
    Task<ServiceResult<bool>> EnterMaintenanceAsync(
        Guid organizationId,
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Takes a node out of maintenance mode.
    /// Organization ownership is verified internally.
    /// </summary>
    Task<ServiceResult<bool>> ExitMaintenanceAsync(
        Guid organizationId,
        Guid nodeId,
        CancellationToken ct = default);
}
