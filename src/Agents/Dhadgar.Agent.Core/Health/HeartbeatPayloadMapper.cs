using Dhadgar.Contracts.Nodes;

namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Maps the agent's internal <see cref="HeartbeatPayload"/> to the hub contract <see cref="AgentHeartbeat"/>.
/// Applies the limits Nodes enforces so that a real heartbeat is never rejected as <c>invalid_heartbeat</c>.
/// </summary>
public static class HeartbeatPayloadMapper
{
    /// <summary>
    /// Converts a heartbeat payload to the hub contract, clamping percentages to 0-100, replacing control
    /// characters in the version and the health issues, and bounding the version, the health issues and the
    /// active game server count.
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
            .Select(w => Sanitize(w, AgentHeartbeatLimits.MaxHealthIssueLength))
            .Where(w => w.Length > 0)
            .Take(AgentHeartbeatLimits.MaxHealthIssues)
            .ToList();

        return new AgentHeartbeat(
            CpuUsagePercent: Clamp(payload.Metrics?.CpuUsagePercent),
            MemoryUsagePercent: Clamp(payload.Metrics?.MemoryUsagePercent),
            DiskUsagePercent: Clamp(diskUsage),
            ActiveGameServers: activeGameServers,
            AgentVersion: Sanitize(payload.AgentVersion, AgentHeartbeatLimits.MaxAgentVersionLength),
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
    /// Replaces each control character with a space, then truncates to the maximum length.
    /// A cut never leaves a lone high surrogate at the end of the string.
    /// </summary>
    private static string Sanitize(string value, int maxLength)
    {
        var cleaned = string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsControl(source[i]) ? ' ' : source[i];
            }
        });

        if (cleaned.Length <= maxLength)
        {
            return cleaned;
        }

        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(cleaned[length - 1]))
        {
            length--;
        }

        return cleaned[..length];
    }
}
