using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Nodes.Models;

/// <summary>
/// Heartbeat request from an agent with health metrics.
/// </summary>
public sealed record HeartbeatRequest(
    [Range(0, 100, ErrorMessage = "CpuUsagePercent must be between 0 and 100")]
    double CpuUsagePercent,

    [Range(0, 100, ErrorMessage = "MemoryUsagePercent must be between 0 and 100")]
    double MemoryUsagePercent,

    [Range(0, 100, ErrorMessage = "DiskUsagePercent must be between 0 and 100")]
    double DiskUsagePercent,

    [Range(0, int.MaxValue, ErrorMessage = "ActiveGameServers cannot be negative")]
    int ActiveGameServers,

    [StringLength(50, ErrorMessage = "AgentVersion cannot exceed 50 characters")]
    string? AgentVersion,

    IReadOnlyList<string>? HealthIssues);

/// <summary>
/// Response to a heartbeat request.
/// </summary>
public sealed record HeartbeatResponse(
    bool Acknowledged,
    DateTime ServerTime);
