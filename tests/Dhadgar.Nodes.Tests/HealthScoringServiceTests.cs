using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Tests;

public sealed class HealthScoringServiceTests
{
    private static IOptions<NodesOptions> CreateOptions(HealthScoringOptions? healthScoring = null)
    {
        var options = new NodesOptions
        {
            HealthScoring = healthScoring ?? new HealthScoringOptions()
        };
        return Options.Create(options);
    }

    private static HealthScoringService CreateService(HealthScoringOptions? healthScoring = null)
    {
        return new HealthScoringService(
            CreateOptions(healthScoring),
            NullLogger<HealthScoringService>.Instance);
    }

    #region CalculateHealthScore Tests

    [Fact]
    public void CalculateHealthScore_PerfectHealth_Returns100()
    {
        // Arrange
        var service = CreateService();

        // Act
        var score = service.CalculateHealthScore(
            cpuPercent: 0,
            memoryPercent: 0,
            diskPercent: 0,
            issueCount: 0);

        // Assert
        Assert.Equal(100, score);
    }

    [Fact]
    public void CalculateHealthScore_AllAtMax_ReturnsMinimum()
    {
        // Arrange
        var service = CreateService();

        // Act
        var score = service.CalculateHealthScore(
            cpuPercent: 100,
            memoryPercent: 100,
            diskPercent: 100,
            issueCount: 5); // 5 issues at 20 penalty each = 0 issue score

        // Assert
        Assert.Equal(0, score);
    }

    [Fact]
    public void CalculateHealthScore_TypicalHealthyNode_ReturnsHighScore()
    {
        // Arrange
        var service = CreateService();

        // Act - typical healthy node: 30% CPU, 50% memory, 40% disk, no issues
        var score = service.CalculateHealthScore(
            cpuPercent: 30,
            memoryPercent: 50,
            diskPercent: 40,
            issueCount: 0);

        // Assert
        // CPU: (100-30) * 0.25 = 17.5
        // Memory: (100-50) * 0.30 = 15
        // Disk: (100-40) * 0.20 = 12
        // Issues: 100 * 0.25 = 25
        // Total: 69.5 -> 70 (rounded)
        Assert.Equal(70, score);
    }

    [Fact]
    public void CalculateHealthScore_DegradedNode_ReturnsMidScore()
    {
        // Arrange
        var service = CreateService();

        // Act - degraded node: 80% CPU, 75% memory, 60% disk, 1 issue
        var score = service.CalculateHealthScore(
            cpuPercent: 80,
            memoryPercent: 75,
            diskPercent: 60,
            issueCount: 1);

        // Assert
        // CPU: (100-80) * 0.25 = 5
        // Memory: (100-75) * 0.30 = 7.5
        // Disk: (100-60) * 0.20 = 8
        // Issues: (100-20) * 0.25 = 20
        // Total: 40.5 -> 40 (banker's rounding - round half to even)
        Assert.Equal(40, score);
    }

    [Fact]
    public void CalculateHealthScore_WithSingleIssue_DeductsCorrectPenalty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var scoreWithoutIssue = service.CalculateHealthScore(0, 0, 0, 0);
        var scoreWithIssue = service.CalculateHealthScore(0, 0, 0, 1);

        // Assert - 1 issue should reduce score by (20 * 0.25) = 5 points
        Assert.Equal(100, scoreWithoutIssue);
        Assert.Equal(95, scoreWithIssue);
    }

    [Fact]
    public void CalculateHealthScore_WithMultipleIssues_CapsIssuePenalty()
    {
        // Arrange
        var service = CreateService();

        // Act - 5 issues should max out the penalty (5 * 20 = 100)
        var scoreWith5Issues = service.CalculateHealthScore(0, 0, 0, 5);
        var scoreWith10Issues = service.CalculateHealthScore(0, 0, 0, 10);

        // Assert - both should have 0 issue score contribution
        // CPU: 100 * 0.25 = 25
        // Memory: 100 * 0.30 = 30
        // Disk: 100 * 0.20 = 20
        // Issues: 0 * 0.25 = 0
        // Total: 75
        Assert.Equal(75, scoreWith5Issues);
        Assert.Equal(75, scoreWith10Issues); // Capped at same value
    }

    [Fact]
    public void CalculateHealthScore_NegativeInputs_ClampsToZero()
    {
        // Arrange
        var service = CreateService();

        // Act - negative values should be treated as 0
        var score = service.CalculateHealthScore(
            cpuPercent: -10,
            memoryPercent: -5,
            diskPercent: -20,
            issueCount: -3);

        // Assert
        Assert.Equal(100, score);
    }

    [Fact]
    public void CalculateHealthScore_OverflowInputs_ClampsTo100()
    {
        // Arrange
        var service = CreateService();

        // Act - values over 100 should be treated as 100
        var score = service.CalculateHealthScore(
            cpuPercent: 150,
            memoryPercent: 200,
            diskPercent: 120,
            issueCount: 0);

        // Assert - all resource scores should be 0, issue score 25
        Assert.Equal(25, score);
    }

    [Fact]
    public void CalculateHealthScore_CustomWeights_AppliesCorrectly()
    {
        // Arrange
        var customOptions = new HealthScoringOptions
        {
            CpuWeight = 0.40,
            MemoryWeight = 0.40,
            DiskWeight = 0.10,
            IssueWeight = 0.10
        };
        var service = CreateService(customOptions);

        // Act - 50% CPU and memory, 0% disk, no issues
        var score = service.CalculateHealthScore(
            cpuPercent: 50,
            memoryPercent: 50,
            diskPercent: 0,
            issueCount: 0);

        // Assert
        // CPU: (100-50) * 0.40 = 20
        // Memory: (100-50) * 0.40 = 20
        // Disk: 100 * 0.10 = 10
        // Issues: 100 * 0.10 = 10
        // Total: 60
        Assert.Equal(60, score);
    }

    [Fact]
    public void CalculateHealthScore_CustomIssuePenalty_AppliesCorrectly()
    {
        // Arrange
        var customOptions = new HealthScoringOptions
        {
            IssueScorePenalty = 50 // Each issue costs 50 points
        };
        var service = CreateService(customOptions);

        // Act
        var scoreWith1Issue = service.CalculateHealthScore(0, 0, 0, 1);
        var scoreWith2Issues = service.CalculateHealthScore(0, 0, 0, 2);

        // Assert
        // With 1 issue: Issue score = 50, contribution = 50 * 0.25 = 12.5
        // Total: 25 + 30 + 20 + 12.5 = 87.5 -> 88
        Assert.Equal(88, scoreWith1Issue);

        // With 2 issues: Issue score = 0, contribution = 0
        // Total: 25 + 30 + 20 + 0 = 75
        Assert.Equal(75, scoreWith2Issues);
    }

    #endregion

    #region DetermineHealthTrend Tests

    [Fact]
    public void DetermineHealthTrend_ScoreIncreased_ReturnsImproving()
    {
        // Arrange
        var service = CreateService();

        // Act
        var trend = service.DetermineHealthTrend(
            currentScore: 80,
            previousScore: 70,
            previousTrend: HealthTrend.Stable);

        // Assert
        Assert.Equal(HealthTrend.Improving, trend);
    }

    [Fact]
    public void DetermineHealthTrend_ScoreDecreased_ReturnsDeclining()
    {
        // Arrange
        var service = CreateService();

        // Act
        var trend = service.DetermineHealthTrend(
            currentScore: 60,
            previousScore: 75,
            previousTrend: HealthTrend.Stable);

        // Assert
        Assert.Equal(HealthTrend.Declining, trend);
    }

    [Fact]
    public void DetermineHealthTrend_ScoreUnchanged_ReturnsStable()
    {
        // Arrange
        var service = CreateService();

        // Act
        var trend = service.DetermineHealthTrend(
            currentScore: 80,
            previousScore: 80,
            previousTrend: HealthTrend.Improving);

        // Assert
        Assert.Equal(HealthTrend.Stable, trend);
    }

    [Fact]
    public void DetermineHealthTrend_SmallChange_ReturnsStable()
    {
        // Arrange - default threshold is 5
        var service = CreateService();

        // Act - change of 4 (less than threshold)
        var trend = service.DetermineHealthTrend(
            currentScore: 84,
            previousScore: 80,
            previousTrend: HealthTrend.Declining);

        // Assert
        Assert.Equal(HealthTrend.Stable, trend);
    }

    [Fact]
    public void DetermineHealthTrend_ExactThreshold_ReturnsChangingTrend()
    {
        // Arrange - default threshold is 5
        var service = CreateService();

        // Act - change of exactly 5 (at threshold)
        var trend = service.DetermineHealthTrend(
            currentScore: 85,
            previousScore: 80,
            previousTrend: HealthTrend.Stable);

        // Assert
        Assert.Equal(HealthTrend.Improving, trend);
    }

    [Fact]
    public void DetermineHealthTrend_CustomThreshold_AppliesCorrectly()
    {
        // Arrange
        var customOptions = new HealthScoringOptions
        {
            TrendThreshold = 10
        };
        var service = CreateService(customOptions);

        // Act - change of 8 (less than custom threshold of 10)
        var trend = service.DetermineHealthTrend(
            currentScore: 88,
            previousScore: 80,
            previousTrend: HealthTrend.Declining);

        // Assert
        Assert.Equal(HealthTrend.Stable, trend);
    }

    #endregion

    #region ShouldTransitionStatus Tests

    [Fact]
    public void ShouldTransitionStatus_HighScore_TransitionsToOnline()
    {
        // Arrange
        var service = CreateService();

        // Act - score 85 is >= 80 (healthy threshold)
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Degraded, 85);

        // Assert
        Assert.Equal(NodeStatus.Online, newStatus);
    }

    [Fact]
    public void ShouldTransitionStatus_MidScore_TransitionsToDegraded()
    {
        // Arrange
        var service = CreateService();

        // Act - score 65 is >= 50 but < 80
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Online, 65);

        // Assert
        Assert.Equal(NodeStatus.Degraded, newStatus);
    }

    [Fact]
    public void ShouldTransitionStatus_LowScore_StaysDegraded()
    {
        // Arrange
        var service = CreateService();

        // Act - score 40 is < 50 (critical), but we don't have a Critical status
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Online, 40);

        // Assert - critical still maps to Degraded
        Assert.Equal(NodeStatus.Degraded, newStatus);
    }

    [Fact]
    public void ShouldTransitionStatus_MaintenanceNode_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act - maintenance nodes should not auto-transition
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Maintenance, 85);

        // Assert
        Assert.Null(newStatus);
    }

    [Fact]
    public void ShouldTransitionStatus_DecommissionedNode_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Decommissioned, 85);

        // Assert
        Assert.Null(newStatus);
    }

    [Fact]
    public void ShouldTransitionStatus_EnrollingNode_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Enrolling, 85);

        // Assert
        Assert.Null(newStatus);
    }

    [Fact]
    public void ShouldTransitionStatus_AlreadyAtTargetStatus_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act - already online with healthy score
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Online, 85);

        // Assert
        Assert.Null(newStatus);
    }

    [Fact]
    public void ShouldTransitionStatus_CustomThresholds_AppliesCorrectly()
    {
        // Arrange
        var customOptions = new HealthScoringOptions
        {
            HealthyThreshold = 90,
            DegradedThreshold = 60
        };
        var service = CreateService(customOptions);

        // Act - score 85 is now degraded (< 90)
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Online, 85);

        // Assert
        Assert.Equal(NodeStatus.Degraded, newStatus);
    }

    [Fact]
    public void ShouldTransitionStatus_ExactlyAtThreshold_ConsideredHealthy()
    {
        // Arrange
        var service = CreateService();

        // Act - score exactly at healthy threshold
        var newStatus = service.ShouldTransitionStatus(NodeStatus.Degraded, 80);

        // Assert
        Assert.Equal(NodeStatus.Online, newStatus);
    }

    #endregion

    #region GetHealthCategory Tests

    [Theory]
    [InlineData(100, HealthCategory.Healthy)]
    [InlineData(80, HealthCategory.Healthy)]
    [InlineData(79, HealthCategory.Degraded)]
    [InlineData(50, HealthCategory.Degraded)]
    [InlineData(49, HealthCategory.Critical)]
    [InlineData(0, HealthCategory.Critical)]
    public void GetHealthCategory_ReturnsCorrectCategory(int score, HealthCategory expectedCategory)
    {
        // Arrange
        var service = CreateService();

        // Act
        var category = service.GetHealthCategory(score);

        // Assert
        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void GetHealthCategory_CustomThresholds_AppliesCorrectly()
    {
        // Arrange
        var customOptions = new HealthScoringOptions
        {
            HealthyThreshold = 95,
            DegradedThreshold = 70
        };
        var service = CreateService(customOptions);

        // Act & Assert
        Assert.Equal(HealthCategory.Healthy, service.GetHealthCategory(95));
        Assert.Equal(HealthCategory.Degraded, service.GetHealthCategory(94));
        Assert.Equal(HealthCategory.Degraded, service.GetHealthCategory(70));
        Assert.Equal(HealthCategory.Critical, service.GetHealthCategory(69));
    }

    #endregion
}
