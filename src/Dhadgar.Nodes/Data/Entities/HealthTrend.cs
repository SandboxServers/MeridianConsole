namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Indicates the direction of health score changes over time.
/// </summary>
public enum HealthTrend
{
    /// <summary>Health score has not changed significantly (within threshold).</summary>
    Stable = 0,

    /// <summary>Health score is increasing (getting healthier).</summary>
    Improving = 1,

    /// <summary>Health score is decreasing (getting less healthy).</summary>
    Declining = 2
}
