using Dhadgar.Nodes.Models;

namespace Dhadgar.Nodes.Services;

public interface IHeartbeatService
{
    /// <summary>
    /// Processes a heartbeat from an agent, updating node health and status.
    /// </summary>
    Task<ServiceResult<bool>> ProcessHeartbeatAsync(
        Guid nodeId,
        HeartbeatRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Checks for nodes that haven't sent heartbeats within the threshold
    /// and marks them as offline.
    /// </summary>
    Task<int> CheckStaleNodesAsync(CancellationToken ct = default);
}
