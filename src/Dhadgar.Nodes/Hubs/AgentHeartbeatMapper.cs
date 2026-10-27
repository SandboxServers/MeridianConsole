using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Models;

namespace Dhadgar.Nodes.Hubs;

/// <summary>
/// Maps the wire heartbeat onto the Nodes-local <see cref="HeartbeatRequest"/> used by <c>IHeartbeatService</c>.
/// </summary>
public static class AgentHeartbeatMapper
{
    /// <summary>One-to-one field copy.</summary>
    public static HeartbeatRequest ToHeartbeatRequest(AgentHeartbeat h) =>
        new(
            h.CpuUsagePercent,
            h.MemoryUsagePercent,
            h.DiskUsagePercent,
            h.ActiveGameServers,
            h.AgentVersion,
            h.HealthIssues);
}
