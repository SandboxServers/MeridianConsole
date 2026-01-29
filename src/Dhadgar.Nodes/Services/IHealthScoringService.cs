using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Services;

/// <summary>
/// Service for calculating node health scores and determining health trends.
/// </summary>
public interface IHealthScoringService
{
    /// <summary>
    /// Calculates a composite health score based on resource usage and health issues.
    /// </summary>
    /// <param name="cpuPercent">CPU usage percentage (0-100).</param>
    /// <param name="memoryPercent">Memory usage percentage (0-100).</param>
    /// <param name="diskPercent">Disk usage percentage (0-100).</param>
    /// <param name="issueCount">Number of active health issues.</param>
    /// <returns>Health score from 0-100 (100 = perfect health).</returns>
    int CalculateHealthScore(double cpuPercent, double memoryPercent, double diskPercent, int issueCount);

    /// <summary>
    /// Determines the health trend based on score changes.
    /// </summary>
    /// <param name="currentScore">The newly calculated health score.</param>
    /// <param name="previousScore">The previous health score.</param>
    /// <param name="previousTrend">The previous health trend.</param>
    /// <returns>The new health trend.</returns>
    HealthTrend DetermineHealthTrend(int currentScore, int previousScore, HealthTrend previousTrend);

    /// <summary>
    /// Determines if the node should transition to a different status based on health score.
    /// </summary>
    /// <param name="currentStatus">The current node status.</param>
    /// <param name="newScore">The newly calculated health score.</param>
    /// <returns>The new status if a transition should occur, or null if no transition needed.</returns>
    NodeStatus? ShouldTransitionStatus(NodeStatus currentStatus, int newScore);

    /// <summary>
    /// Gets the status category for a given health score.
    /// </summary>
    /// <param name="score">The health score.</param>
    /// <returns>Healthy, Degraded, or Critical based on thresholds.</returns>
    HealthCategory GetHealthCategory(int score);
}

/// <summary>
/// Health category based on score thresholds.
/// </summary>
public enum HealthCategory
{
    /// <summary>Score is at or above the healthy threshold.</summary>
    Healthy,

    /// <summary>Score is between degraded and healthy thresholds.</summary>
    Degraded,

    /// <summary>Score is below the degraded threshold.</summary>
    Critical
}
