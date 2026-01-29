using Dhadgar.Nodes.Data.Entities;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services;

/// <summary>
/// Service for calculating node health scores and determining health trends.
/// </summary>
public sealed class HealthScoringService : IHealthScoringService
{
    private readonly HealthScoringOptions _options;
    private readonly ILogger<HealthScoringService> _logger;

    public HealthScoringService(
        IOptions<NodesOptions> options,
        ILogger<HealthScoringService> logger)
    {
        _options = options.Value.HealthScoring;
        _logger = logger;
    }

    /// <inheritdoc />
    public int CalculateHealthScore(double cpuPercent, double memoryPercent, double diskPercent, int issueCount)
    {
        // Clamp input values to valid ranges
        cpuPercent = Math.Clamp(cpuPercent, 0, 100);
        memoryPercent = Math.Clamp(memoryPercent, 0, 100);
        diskPercent = Math.Clamp(diskPercent, 0, 100);
        issueCount = Math.Max(0, issueCount);

        // Calculate individual component scores (higher usage = lower score)
        var cpuScore = 100.0 - cpuPercent;
        var memoryScore = 100.0 - memoryPercent;
        var diskScore = 100.0 - diskPercent;

        // Calculate issue score (each issue reduces score by penalty amount)
        var issuePenalty = issueCount * _options.IssueScorePenalty;
        var issueScore = Math.Max(0.0, 100.0 - issuePenalty);

        // Calculate weighted composite score
        var compositeScore =
            (cpuScore * _options.CpuWeight) +
            (memoryScore * _options.MemoryWeight) +
            (diskScore * _options.DiskWeight) +
            (issueScore * _options.IssueWeight);

        // Round and clamp to valid range
        var finalScore = (int)Math.Round(Math.Clamp(compositeScore, 0, 100));

        _logger.LogTrace(
            "Calculated health score: {Score} (CPU: {CpuScore}, Memory: {MemoryScore}, Disk: {DiskScore}, Issues: {IssueScore})",
            finalScore, cpuScore, memoryScore, diskScore, issueScore);

        return finalScore;
    }

    /// <inheritdoc />
    public HealthTrend DetermineHealthTrend(int currentScore, int previousScore, HealthTrend previousTrend)
    {
        var scoreDiff = currentScore - previousScore;

        // If the change is within the threshold, maintain stability or slight trend
        if (Math.Abs(scoreDiff) < _options.TrendThreshold)
        {
            return HealthTrend.Stable;
        }

        // Determine trend based on score change direction
        return scoreDiff > 0 ? HealthTrend.Improving : HealthTrend.Declining;
    }

    /// <inheritdoc />
    public NodeStatus? ShouldTransitionStatus(NodeStatus currentStatus, int newScore)
    {
        // Don't auto-transition from these states
        if (currentStatus is NodeStatus.Maintenance or NodeStatus.Decommissioned or NodeStatus.Enrolling)
        {
            return null;
        }

        var category = GetHealthCategory(newScore);

        // Determine target status based on category
        var targetStatus = category switch
        {
            HealthCategory.Healthy => NodeStatus.Online,
            HealthCategory.Degraded => NodeStatus.Degraded,
            HealthCategory.Critical => NodeStatus.Degraded, // Critical still maps to Degraded status
            _ => currentStatus
        };

        // Only return a status if it's different from current
        return targetStatus != currentStatus ? targetStatus : null;
    }

    /// <inheritdoc />
    public HealthCategory GetHealthCategory(int score)
    {
        if (score >= _options.HealthyThreshold)
        {
            return HealthCategory.Healthy;
        }

        if (score >= _options.DegradedThreshold)
        {
            return HealthCategory.Degraded;
        }

        return HealthCategory.Critical;
    }
}
