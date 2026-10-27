using Dhadgar.Contracts.Nodes;

namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Maps the agent's internal <see cref="HeartbeatPayload"/> to the hub contract <see cref="AgentHeartbeat"/>.
/// Applies the limits Nodes enforces so that a real heartbeat is never rejected as <c>invalid_heartbeat</c>.
/// </summary>
public static class HeartbeatPayloadMapper
{
    /// <summary>
    /// Converts a heartbeat payload to the hub contract, clamping percentages to 0-100 and bounding
    /// the version, the health issues and the active game server count.
    /// </summary>
    /// <param name="payload">The payload collected by the health reporter.</param>
    /// <returns>The contract heartbeat.</returns>
    public static AgentHeartbeat ToContract(HeartbeatPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var disks = payload.Metrics?.Disks;
        var diskUsage = disks is { Count: > 0 }
            ? disks.Max(d => Clamp(d.UsagePercent))
            : 0;

        var activeGameServers = Math.Min(
            payload.ActiveProcesses.Count(p => p.State == ProcessState.Running),
            AgentHeartbeatLimits.MaxActiveGameServers);

        var healthIssues = payload.Warnings
            .Where(w => w is not null)
            .Take(AgentHeartbeatLimits.MaxHealthIssues)
            .Select(w => Truncate(w, AgentHeartbeatLimits.MaxHealthIssueLength))
            .ToList();

        return new AgentHeartbeat(
            CpuUsagePercent: Clamp(payload.Metrics?.CpuUsagePercent),
            MemoryUsagePercent: Clamp(payload.Metrics?.MemoryUsagePercent),
            DiskUsagePercent: Clamp(diskUsage),
            ActiveGameServers: activeGameServers,
            AgentVersion: Truncate(payload.AgentVersion, AgentHeartbeatLimits.MaxAgentVersionLength),
            HealthIssues: healthIssues);
    }

    /// <summary>
    /// Maps null and NaN to 0 and clamps the value to the 0-100 range.
    /// </summary>
    private static double Clamp(double? value)
    {
        if (value is not { } v || double.IsNaN(v))
        {
            return 0;
        }

        return Math.Clamp(v, 0, 100);
    }

    /// <summary>
    /// Truncates a string to the maximum length.
    /// </summary>
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
