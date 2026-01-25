using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Nodes.Tests;

public sealed class HeartbeatServiceTests
{
    private static readonly Guid TestOrgId = Guid.NewGuid();

    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NodesDbContext(options);
    }

    private static IOptions<NodesOptions> CreateOptions() =>
        Options.Create(new NodesOptions());

    private static IHealthScoringService CreateHealthScoringService()
    {
        return new HealthScoringService(
            CreateOptions(),
            NullLogger<HealthScoringService>.Instance);
    }

    private static (HeartbeatService Service, TestNodesEventPublisher Publisher) CreateService(
        NodesDbContext context,
        FakeTimeProvider? timeProvider = null)
    {
        var publisher = new TestNodesEventPublisher();
        var service = new HeartbeatService(
            context,
            publisher,
            timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow),
            CreateOptions(),
            NullLogger<HeartbeatService>.Instance,
            CreateHealthScoringService());
        return (service, publisher);
    }

    private static async Task<Node> SeedNodeAsync(
        NodesDbContext context,
        NodeStatus status = NodeStatus.Online,
        DateTime? lastHeartbeat = null)
    {
        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = TestOrgId,
            Name = $"node-{Guid.NewGuid():N}",
            Status = status,
            Platform = "linux",
            AgentVersion = "1.0.0",
            LastHeartbeat = lastHeartbeat,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        context.Nodes.Add(node);
        await context.SaveChangesAsync();
        return node;
    }

    private static HeartbeatRequest CreateHealthyHeartbeat(string? agentVersion = null) => new(
        CpuUsagePercent: 20.0,  // Low usage values to produce a healthy score (>=80)
        MemoryUsagePercent: 20.0,
        DiskUsagePercent: 20.0,
        ActiveGameServers: 2,
        AgentVersion: agentVersion,
        HealthIssues: null);

    private static HeartbeatRequest CreateDegradedHeartbeat() => new(
        CpuUsagePercent: 95.0, // High CPU
        MemoryUsagePercent: 50.0,
        DiskUsagePercent: 40.0,
        ActiveGameServers: 5,
        AgentVersion: "1.0.1",
        HealthIssues: ["High CPU usage detected"]);

    [Fact]
    public async Task ProcessHeartbeatAsync_UpdatesLastHeartbeat()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, _) = CreateService(context, timeProvider);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        // Act
        var result = await service.ProcessHeartbeatAsync(node.Id, CreateHealthyHeartbeat());

        // Assert
        Assert.True(result.Success);
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(now.UtcDateTime, updated!.LastHeartbeat);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_UpdatesAgentVersion()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, CreateHealthyHeartbeat("2.0.0"));

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal("2.0.0", updated!.AgentVersion);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_CreatesHealthRecord()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 45.5,
            MemoryUsagePercent: 60.0,
            DiskUsagePercent: 30.0,
            ActiveGameServers: 3,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var health = await context.NodeHealths.FirstOrDefaultAsync(h => h.NodeId == node.Id);
        Assert.NotNull(health);
        Assert.Equal(45.5, health.CpuUsagePercent);
        Assert.Equal(60.0, health.MemoryUsagePercent);
        Assert.Equal(30.0, health.DiskUsagePercent);
        Assert.Equal(3, health.ActiveGameServers);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_UpdatesExistingHealthRecord()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // Create initial health record
        var existingHealth = new NodeHealth
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            CpuUsagePercent = 10.0,
            MemoryUsagePercent = 20.0,
            DiskUsagePercent = 15.0,
            ActiveGameServers = 1,
            ReportedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        context.NodeHealths.Add(existingHealth);
        await context.SaveChangesAsync();

        var request = new HeartbeatRequest(
            CpuUsagePercent: 80.0,
            MemoryUsagePercent: 75.0,
            DiskUsagePercent: 50.0,
            ActiveGameServers: 8,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var healthRecords = await context.NodeHealths.Where(h => h.NodeId == node.Id).ToListAsync();
        Assert.Single(healthRecords);
        Assert.Equal(80.0, healthRecords[0].CpuUsagePercent);
        Assert.Equal(8, healthRecords[0].ActiveGameServers);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_HealthyMetrics_SetsOnlineStatus()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Offline);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, CreateHealthyHeartbeat());

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Online, updated!.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_HighCpu_SetsDegradedStatus()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 95.0, // >= 90% threshold
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 40.0,
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Degraded, updated!.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_HighMemory_SetsDegradedStatus()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 50.0,
            MemoryUsagePercent: 92.0, // >= 90% threshold
            DiskUsagePercent: 40.0,
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Degraded, updated!.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_HighDisk_SetsDegradedStatus()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 50.0,
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 95.0, // >= 90% threshold
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Degraded, updated!.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_WithHealthIssues_SetsDegradedStatus()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 30.0,
            MemoryUsagePercent: 30.0,
            DiskUsagePercent: 30.0,
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: ["Network latency detected", "Disk SMART warning"]);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Degraded, updated!.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_MaintenanceNode_PreservesMaintenanceStatus()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Maintenance);

        // Act - even with healthy metrics
        await service.ProcessHeartbeatAsync(node.Id, CreateHealthyHeartbeat());

        // Assert - should stay in maintenance
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Maintenance, updated!.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_DecommissionedNode_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Decommissioned);

        // Act
        var result = await service.ProcessHeartbeatAsync(node.Id, CreateHealthyHeartbeat());

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_decommissioned", result.Error);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_NonExistentNode_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);

        // Act
        var result = await service.ProcessHeartbeatAsync(Guid.NewGuid(), CreateHealthyHeartbeat());

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_not_found", result.Error);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_NodeComesOnline_PublishesEvent()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Offline);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, CreateHealthyHeartbeat());

        // Assert
        Assert.True(publisher.HasMessage<NodeOnline>());
        var evt = publisher.GetLastMessage<NodeOnline>()!;
        Assert.Equal(node.Id, evt.NodeId);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_NodeBecomesDegraded_PublishesEvent()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, CreateDegradedHeartbeat());

        // Assert
        Assert.True(publisher.HasMessage<NodeDegraded>());
        var evt = publisher.GetLastMessage<NodeDegraded>()!;
        Assert.Equal(node.Id, evt.NodeId);
        Assert.Contains("High CPU usage detected", evt.Issues);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_NodeRecovers_PublishesEvent()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Degraded);

        // Act - healthy heartbeat
        await service.ProcessHeartbeatAsync(node.Id, CreateHealthyHeartbeat());

        // Assert
        Assert.True(publisher.HasMessage<NodeRecovered>());
        var evt = publisher.GetLastMessage<NodeRecovered>()!;
        Assert.Equal(node.Id, evt.NodeId);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_EnrollingNode_TransitionsToOnline()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Enrolling);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, CreateHealthyHeartbeat());

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Online, updated!.Status);
        Assert.True(publisher.HasMessage<NodeOnline>());
    }

    [Fact]
    public async Task CheckStaleNodesAsync_MarksStaleNodesOffline()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, publisher) = CreateService(context, timeProvider);

        // Create nodes with various heartbeat times
        var freshNode = await SeedNodeAsync(context, NodeStatus.Online,
            lastHeartbeat: now.UtcDateTime.AddMinutes(-2)); // Fresh
        var staleNode = await SeedNodeAsync(context, NodeStatus.Online,
            lastHeartbeat: now.UtcDateTime.AddMinutes(-10)); // Stale (> 5 min threshold)
        var veryStaleNode = await SeedNodeAsync(context, NodeStatus.Degraded,
            lastHeartbeat: now.UtcDateTime.AddHours(-1)); // Very stale
        var nullHeartbeat = await SeedNodeAsync(context, NodeStatus.Online,
            lastHeartbeat: null); // Never had heartbeat

        // Act
        var count = await service.CheckStaleNodesAsync();

        // Assert
        Assert.Equal(3, count); // staleNode, veryStaleNode, nullHeartbeat

        var freshUpdated = await context.Nodes.FindAsync(freshNode.Id);
        Assert.Equal(NodeStatus.Online, freshUpdated!.Status);

        var staleUpdated = await context.Nodes.FindAsync(staleNode.Id);
        Assert.Equal(NodeStatus.Offline, staleUpdated!.Status);

        var veryStaleUpdated = await context.Nodes.FindAsync(veryStaleNode.Id);
        Assert.Equal(NodeStatus.Offline, veryStaleUpdated!.Status);

        var nullUpdated = await context.Nodes.FindAsync(nullHeartbeat.Id);
        Assert.Equal(NodeStatus.Offline, nullUpdated!.Status);
    }

    [Fact]
    public async Task CheckStaleNodesAsync_PublishesEventsForEachStaleNode()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, publisher) = CreateService(context, timeProvider);

        var stale1 = await SeedNodeAsync(context, NodeStatus.Online,
            lastHeartbeat: now.UtcDateTime.AddMinutes(-10));
        var stale2 = await SeedNodeAsync(context, NodeStatus.Online,
            lastHeartbeat: now.UtcDateTime.AddMinutes(-15));

        // Act
        await service.CheckStaleNodesAsync();

        // Assert
        var events = publisher.GetMessages<NodeOffline>();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.NodeId == stale1.Id);
        Assert.Contains(events, e => e.NodeId == stale2.Id);
        Assert.All(events, e => Assert.Equal("Heartbeat timeout", e.Reason));
    }

    [Fact]
    public async Task CheckStaleNodesAsync_IgnoresMaintenanceNodes()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, _) = CreateService(context, timeProvider);

        var maintenanceNode = await SeedNodeAsync(context, NodeStatus.Maintenance,
            lastHeartbeat: now.UtcDateTime.AddHours(-1)); // Old but in maintenance

        // Act
        var count = await service.CheckStaleNodesAsync();

        // Assert
        Assert.Equal(0, count);
        var updated = await context.Nodes.FindAsync(maintenanceNode.Id);
        Assert.Equal(NodeStatus.Maintenance, updated!.Status);
    }

    [Fact]
    public async Task CheckStaleNodesAsync_IgnoresOfflineNodes()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, _) = CreateService(context, timeProvider);

        await SeedNodeAsync(context, NodeStatus.Offline,
            lastHeartbeat: now.UtcDateTime.AddHours(-1)); // Already offline

        // Act
        var count = await service.CheckStaleNodesAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CheckStaleNodesAsync_NoStaleNodes_ReturnsZero()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, publisher) = CreateService(context, timeProvider);

        await SeedNodeAsync(context, NodeStatus.Online,
            lastHeartbeat: now.UtcDateTime.AddMinutes(-1));
        await SeedNodeAsync(context, NodeStatus.Online,
            lastHeartbeat: now.UtcDateTime.AddMinutes(-3));

        // Act
        var count = await service.CheckStaleNodesAsync();

        // Assert
        Assert.Equal(0, count);
        Assert.False(publisher.HasMessage<NodeOffline>());
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_SerializesHealthIssues()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 30.0,
            MemoryUsagePercent: 30.0,
            DiskUsagePercent: 30.0,
            ActiveGameServers: 0,
            AgentVersion: null,
            HealthIssues: ["Issue 1", "Issue 2"]);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var health = await context.NodeHealths.FirstOrDefaultAsync(h => h.NodeId == node.Id);
        Assert.NotNull(health);
        Assert.Contains("Issue 1", health.HealthIssues);
        Assert.Contains("Issue 2", health.HealthIssues);
    }

    #region Health Scoring Integration Tests

    [Fact]
    public async Task ProcessHeartbeatAsync_CalculatesHealthScore()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        var request = new HeartbeatRequest(
            CpuUsagePercent: 30.0,
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 40.0,
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var health = await context.NodeHealths.FirstOrDefaultAsync(h => h.NodeId == node.Id);
        Assert.NotNull(health);
        // Expected: CPU(70*0.25) + Memory(50*0.30) + Disk(60*0.20) + Issues(100*0.25)
        // = 17.5 + 15 + 12 + 25 = 69.5 -> 70
        Assert.Equal(70, health.HealthScore);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_UpdatesHealthTrend_Improving()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // First heartbeat with worse metrics
        var degradedRequest = new HeartbeatRequest(
            CpuUsagePercent: 80.0,
            MemoryUsagePercent: 80.0,
            DiskUsagePercent: 80.0,
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: null);
        await service.ProcessHeartbeatAsync(node.Id, degradedRequest);

        // Second heartbeat with better metrics
        var improvedRequest = new HeartbeatRequest(
            CpuUsagePercent: 20.0,
            MemoryUsagePercent: 20.0,
            DiskUsagePercent: 20.0,
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, improvedRequest);

        // Assert
        var health = await context.NodeHealths.FirstOrDefaultAsync(h => h.NodeId == node.Id);
        Assert.NotNull(health);
        Assert.Equal(HealthTrend.Improving, health.HealthTrend);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_UpdatesHealthTrend_Declining()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // First heartbeat with good metrics
        var goodRequest = new HeartbeatRequest(
            CpuUsagePercent: 20.0,
            MemoryUsagePercent: 20.0,
            DiskUsagePercent: 20.0,
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: null);
        await service.ProcessHeartbeatAsync(node.Id, goodRequest);

        // Second heartbeat with worse metrics
        var worseRequest = new HeartbeatRequest(
            CpuUsagePercent: 80.0,
            MemoryUsagePercent: 80.0,
            DiskUsagePercent: 80.0,
            ActiveGameServers: 2,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, worseRequest);

        // Assert
        var health = await context.NodeHealths.FirstOrDefaultAsync(h => h.NodeId == node.Id);
        Assert.NotNull(health);
        Assert.Equal(HealthTrend.Declining, health.HealthTrend);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_UpdatesLastScoreChange_WhenScoreChanges()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, _) = CreateService(context, timeProvider);
        var node = await SeedNodeAsync(context);

        // First heartbeat
        var request1 = new HeartbeatRequest(
            CpuUsagePercent: 50.0,
            MemoryUsagePercent: 50.0,
            DiskUsagePercent: 50.0,
            ActiveGameServers: 0,
            AgentVersion: null,
            HealthIssues: null);
        await service.ProcessHeartbeatAsync(node.Id, request1);

        // Advance time and send second heartbeat with different score
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var request2 = new HeartbeatRequest(
            CpuUsagePercent: 70.0,
            MemoryUsagePercent: 70.0,
            DiskUsagePercent: 70.0,
            ActiveGameServers: 0,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request2);

        // Assert
        var health = await context.NodeHealths.FirstOrDefaultAsync(h => h.NodeId == node.Id);
        Assert.NotNull(health);
        Assert.Equal(now.UtcDateTime.AddMinutes(5), health.LastScoreChange);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_LowScore_TransitionsToDegraded()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        // Request with metrics that result in score < 80 (degraded threshold)
        var request = new HeartbeatRequest(
            CpuUsagePercent: 60.0,
            MemoryUsagePercent: 60.0,
            DiskUsagePercent: 60.0,
            ActiveGameServers: 0,
            AgentVersion: null,
            HealthIssues: ["Issue 1"]); // Adds penalty

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Degraded, updated!.Status);
        Assert.True(publisher.HasMessage<NodeDegraded>());
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_HighScore_TransitionsToOnline()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Degraded);

        // Request with metrics that result in score >= 80 (healthy threshold)
        var request = new HeartbeatRequest(
            CpuUsagePercent: 10.0,
            MemoryUsagePercent: 10.0,
            DiskUsagePercent: 10.0,
            ActiveGameServers: 0,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Online, updated!.Status);
        Assert.True(publisher.HasMessage<NodeRecovered>());
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_VeryLowScore_StillDegraded()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        // Request with metrics that result in critical score (< 50)
        var request = new HeartbeatRequest(
            CpuUsagePercent: 95.0,
            MemoryUsagePercent: 95.0,
            DiskUsagePercent: 95.0,
            ActiveGameServers: 0,
            AgentVersion: null,
            HealthIssues: ["Critical issue 1", "Critical issue 2", "Critical issue 3"]);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert - Critical still maps to Degraded status
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Degraded, updated!.Status);

        var health = await context.NodeHealths.FirstOrDefaultAsync(h => h.NodeId == node.Id);
        Assert.NotNull(health);
        Assert.True(health.HealthScore < 50); // Critical range
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_NewNode_DefaultsToScore100()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _) = CreateService(context);
        var node = await SeedNodeAsync(context, NodeStatus.Online);

        // First heartbeat for a node that has no health record
        var request = new HeartbeatRequest(
            CpuUsagePercent: 0,
            MemoryUsagePercent: 0,
            DiskUsagePercent: 0,
            ActiveGameServers: 0,
            AgentVersion: null,
            HealthIssues: null);

        // Act
        await service.ProcessHeartbeatAsync(node.Id, request);

        // Assert
        var health = await context.NodeHealths.FirstOrDefaultAsync(h => h.NodeId == node.Id);
        Assert.NotNull(health);
        Assert.Equal(100, health.HealthScore);
        Assert.Equal(HealthTrend.Stable, health.HealthTrend);
    }

    #endregion
}
