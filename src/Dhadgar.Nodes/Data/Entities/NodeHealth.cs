namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Current health metrics from a node, updated via heartbeats.
/// </summary>
public sealed class NodeHealth
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }

    /// <summary>Current CPU utilization percentage (0-100).</summary>
    public double CpuUsagePercent { get; set; }

    /// <summary>Current memory utilization percentage (0-100).</summary>
    public double MemoryUsagePercent { get; set; }

    /// <summary>Current disk utilization percentage (0-100).</summary>
    public double DiskUsagePercent { get; set; }

    /// <summary>Number of game servers currently running on this node.</summary>
    public int ActiveGameServers { get; set; }

    /// <summary>JSON array of current health issues/warnings.</summary>
    public string? HealthIssues { get; set; }

    /// <summary>When this health data was reported by the agent.</summary>
    public DateTime ReportedAt { get; set; }

    /// <summary>Composite health score (0-100, 100 = perfect health).</summary>
    public int HealthScore { get; set; }

    /// <summary>Direction of health score changes over time.</summary>
    public HealthTrend HealthTrend { get; set; }

    /// <summary>When the health score last changed significantly.</summary>
    public DateTime? LastScoreChange { get; set; }

    // Navigation property
    public Node Node { get; set; } = null!;
}
