using System.Diagnostics;
using System.Text.Json;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services;

public sealed class HeartbeatService : IHeartbeatService
{
    private readonly NodesDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly NodesOptions _options;
    private readonly IHealthScoringService _healthScoringService;

    public HeartbeatService(
        NodesDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        IOptions<NodesOptions> options,
        ILogger<HeartbeatService> logger,
        IHealthScoringService healthScoringService)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
        _healthScoringService = healthScoringService;
    }

    public async Task<ServiceResult<bool>> ProcessHeartbeatAsync(
        Guid nodeId,
        HeartbeatRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var node = await _dbContext.Nodes
            .Include(n => n.Health)
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<bool>("node_not_found");
        }

        // Don't process heartbeats for decommissioned nodes
        if (node.Status == NodeStatus.Decommissioned)
        {
            return ServiceResult.Fail<bool>("node_decommissioned");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var previousStatus = node.Status;
        var wasOffline = previousStatus == NodeStatus.Offline || previousStatus == NodeStatus.Enrolling;

        // Update node
        node.LastHeartbeat = now;
        node.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(request.AgentVersion))
        {
            node.AgentVersion = request.AgentVersion;
        }

        // Update or create health record
        var previousScore = node.Health?.HealthScore ?? 100;
        var previousTrend = node.Health?.HealthTrend ?? HealthTrend.Stable;

        if (node.Health is null)
        {
            node.Health = new NodeHealth
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId
            };
            _dbContext.NodeHealths.Add(node.Health);
        }

        node.Health.CpuUsagePercent = request.CpuUsagePercent;
        node.Health.MemoryUsagePercent = request.MemoryUsagePercent;
        node.Health.DiskUsagePercent = request.DiskUsagePercent;
        node.Health.ActiveGameServers = request.ActiveGameServers;
        node.Health.HealthIssues = request.HealthIssues?.Count > 0
            ? JsonSerializer.Serialize(request.HealthIssues)
            : null;
        node.Health.ReportedAt = now;

        // Calculate health score
        var issueCount = request.HealthIssues?.Count ?? 0;
        var newScore = _healthScoringService.CalculateHealthScore(
            request.CpuUsagePercent,
            request.MemoryUsagePercent,
            request.DiskUsagePercent,
            issueCount);

        // Determine health trend
        var newTrend = _healthScoringService.DetermineHealthTrend(newScore, previousScore, previousTrend);

        // Update health record with score and trend
        node.Health.HealthScore = newScore;
        node.Health.HealthTrend = newTrend;

        // Track score changes
        if (newScore != previousScore)
        {
            node.Health.LastScoreChange = now;
        }

        // Record health trend changes
        if (newTrend != previousTrend)
        {
            NodesMetrics.RecordHealthTrendChange(previousTrend.ToString(), newTrend.ToString());
        }

        // Get health category for metrics
        var healthCategory = _healthScoringService.GetHealthCategory(newScore);

        // Record health score metric
        NodesMetrics.RecordHealthScore(newScore, node.Platform, healthCategory.ToString());

        // Determine new status based on health score (if not in maintenance)
        var healthTransitionRecorded = false;
        if (node.Status != NodeStatus.Maintenance)
        {
            var newStatus = _healthScoringService.ShouldTransitionStatus(node.Status, newScore);
            if (newStatus.HasValue)
            {
                node.Status = newStatus.Value;
                NodesMetrics.RecordHealthStatusTransition(
                    previousStatus.ToString(),
                    newStatus.Value.ToString(),
                    healthCategory.ToString());
                healthTransitionRecorded = true;
            }
            else if (wasOffline)
            {
                // Node coming online from offline/enrolling state
                node.Status = healthCategory == HealthCategory.Healthy ? NodeStatus.Online : NodeStatus.Degraded;
            }
        }

        // Transactional Outbox Pattern: Publish events BEFORE SaveChangesAsync.
        // MassTransit's outbox stores messages in the same transaction as entity changes.
        // When SaveChangesAsync commits, both the entity updates AND the outbox messages
        // are persisted atomically. The outbox delivery service then sends the messages.
        await PublishStatusEventsAsync(nodeId, previousStatus, node.Status, request, now, ct);

        await _dbContext.SaveChangesAsync(ct);

        // Record metrics
        stopwatch.Stop();
        NodesMetrics.RecordHeartbeat(node.Platform, stopwatch.Elapsed.TotalMilliseconds);

        // Record status transition if not already recorded as a health-based transition
        if (previousStatus != node.Status && !healthTransitionRecorded)
        {
            NodesMetrics.RecordStatusTransition(previousStatus.ToString(), node.Status.ToString());
        }

        _logger.LogDebug(
            "Processed heartbeat for node {NodeId}, status: {Status}, score: {Score}, CPU: {Cpu}%, Memory: {Memory}%",
            nodeId, node.Status, newScore, request.CpuUsagePercent, request.MemoryUsagePercent);

        return ServiceResult.Ok(true);
    }

    public async Task<int> CheckStaleNodesAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var threshold = now.AddMinutes(-_options.HeartbeatThresholdMinutes);

        // Find nodes that should be marked offline
        var staleNodes = await _dbContext.Nodes
            .Where(n =>
                n.Status == NodeStatus.Online || n.Status == NodeStatus.Degraded)
            .Where(n =>
                n.LastHeartbeat == null || n.LastHeartbeat < threshold)
            .ToListAsync(ct);

        if (staleNodes.Count == 0)
        {
            return 0;
        }

        // Update each stale node to offline
        foreach (var node in staleNodes)
        {
            node.Status = NodeStatus.Offline;
            node.UpdatedAt = now;
        }

        // Transactional Outbox Pattern: Publish events BEFORE SaveChangesAsync.
        // MassTransit's outbox stores messages in the same transaction as entity changes.
        // When SaveChangesAsync commits, both the entity updates AND the outbox messages
        // are persisted atomically. The outbox delivery service then sends the messages.
        foreach (var node in staleNodes)
        {
            await _publishEndpoint.Publish(
                new NodeOffline(node.Id, now, "Heartbeat timeout"),
                ct);

            _logger.LogWarning("Node {NodeId} marked offline due to heartbeat timeout", node.Id);
        }

        await _dbContext.SaveChangesAsync(ct);

        // Record metrics for stale nodes detected
        NodesMetrics.StaleNodesDetected.Add(staleNodes.Count);

        return staleNodes.Count;
    }

    private async Task PublishStatusEventsAsync(
        Guid nodeId,
        NodeStatus previousStatus,
        NodeStatus newStatus,
        HeartbeatRequest request,
        DateTime timestamp,
        CancellationToken ct)
    {
        // Node came online
        if ((previousStatus == NodeStatus.Offline || previousStatus == NodeStatus.Enrolling) &&
            (newStatus == NodeStatus.Online || newStatus == NodeStatus.Degraded))
        {
            await _publishEndpoint.Publish(new NodeOnline(nodeId, timestamp), ct);
            _logger.LogInformation("Node {NodeId} is now online", nodeId);
        }

        // Node became degraded
        if (previousStatus == NodeStatus.Online && newStatus == NodeStatus.Degraded)
        {
            var issues = request.HealthIssues ?? [];
            await _publishEndpoint.Publish(new NodeDegraded(nodeId, timestamp, issues), ct);
            _logger.LogWarning("Node {NodeId} is degraded: {Issues}", nodeId, string.Join(", ", issues));
        }

        // Node recovered from degraded
        if (previousStatus == NodeStatus.Degraded && newStatus == NodeStatus.Online)
        {
            await _publishEndpoint.Publish(new NodeRecovered(nodeId, timestamp), ct);
            _logger.LogInformation("Node {NodeId} recovered from degraded state", nodeId);
        }
    }
}
