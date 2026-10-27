using Dhadgar.Contracts.Nodes;

namespace Dhadgar.Nodes.Hubs;

/// <summary>
/// Range and size checks on <see cref="AgentHeartbeat"/> before anything is written to the database.
/// </summary>
public static class AgentHeartbeatValidator
{
    /// <summary>Returns true when the heartbeat is within the limits in <see cref="AgentHeartbeatLimits"/>.</summary>
    public static bool IsValid(AgentHeartbeat? h)
    {
        if (h is null)
        {
            return false;
        }

        if (!IsPercentage(h.CpuUsagePercent) ||
            !IsPercentage(h.MemoryUsagePercent) ||
            !IsPercentage(h.DiskUsagePercent))
        {
            return false;
        }

        if (h.ActiveGameServers < 0 || h.ActiveGameServers > AgentHeartbeatLimits.MaxActiveGameServers)
        {
            return false;
        }

        if (h.AgentVersion is not null && h.AgentVersion.Length > AgentHeartbeatLimits.MaxAgentVersionLength)
        {
            return false;
        }

        if (h.HealthIssues is not null)
        {
            if (h.HealthIssues.Count > AgentHeartbeatLimits.MaxHealthIssues)
            {
                return false;
            }

            foreach (var issue in h.HealthIssues)
            {
                if (issue is null || issue.Length > AgentHeartbeatLimits.MaxHealthIssueLength)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsPercentage(double value) =>
        double.IsFinite(value) && value >= 0 && value <= 100;
}
