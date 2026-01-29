using Dhadgar.Contracts;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Nodes.Tests;

public sealed class NodeServiceTests
{
    private static readonly Guid TestOrgId = Guid.NewGuid();

    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new NodesDbContext(options);
    }

    private static IOptions<NodesOptions> CreateOptions() =>
        Options.Create(new NodesOptions { HeartbeatThresholdMinutes = 5 });

    private static (NodeService Service, TestNodesEventPublisher Publisher, TestAuditService Audit) CreateService(
        NodesDbContext context,
        FakeTimeProvider? timeProvider = null)
    {
        var publisher = new TestNodesEventPublisher();
        var auditService = new TestAuditService();
        var service = new NodeService(
            context,
            publisher,
            auditService,
            timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow),
            CreateOptions(),
            NullLogger<NodeService>.Instance);
        return (service, publisher, auditService);
    }

    private static async Task<Node> SeedNodeAsync(
        NodesDbContext context,
        Guid? orgId = null,
        NodeStatus status = NodeStatus.Online,
        string name = "test-node",
        string platform = "linux",
        DateTime? lastHeartbeat = null)
    {
        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId ?? TestOrgId,
            Name = name,
            DisplayName = "Test Node",
            Status = status,
            Platform = platform,
            AgentVersion = "1.0.0",
            LastHeartbeat = lastHeartbeat ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        context.Nodes.Add(node);
        await context.SaveChangesAsync();
        return node;
    }

    [Fact]
    public async Task GetNodesAsync_ReturnsNodesForOrganization()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        await SeedNodeAsync(context, TestOrgId, name: "node-1");
        await SeedNodeAsync(context, TestOrgId, name: "node-2");
        await SeedNodeAsync(context, Guid.NewGuid(), name: "other-org-node"); // Different org

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery());

        // Assert
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, n => Assert.StartsWith("node-", n.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetNodesAsync_SupportsPagination()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        for (int i = 0; i < 10; i++)
        {
            await SeedNodeAsync(context, TestOrgId, name: $"node-{i:D2}");
        }

        // Act
        var page1 = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Page = 1, PageSize = 5 });
        var page2 = await service.GetNodesAsync(TestOrgId, new NodeListQuery { Page = 2, PageSize = 5 });

        // Assert
        Assert.Equal(10, page1.Total);
        Assert.Equal(5, page1.Items.Count);
        Assert.Equal(10, page2.Total);
        Assert.Equal(5, page2.Items.Count);
        Assert.Empty(page1.Items.Select(n => n.Id).Intersect(page2.Items.Select(n => n.Id)));
    }

    [Fact]
    public async Task GetNodeAsync_ExistingNode_ReturnsDetail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // Add related data
        context.HardwareInventories.Add(new NodeHardwareInventory
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Hostname = "test-host",
            CpuCores = 8,
            MemoryBytes = 16L * 1024 * 1024 * 1024,
            DiskBytes = 500L * 1024 * 1024 * 1024,
            CollectedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetNodeAsync(TestOrgId, node.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(node.Id, result.Value.Id);
        Assert.Equal(node.Name, result.Value.Name);
        Assert.NotNull(result.Value.Hardware);
        Assert.Equal("test-host", result.Value.Hardware.Hostname);
    }

    [Fact]
    public async Task GetNodeAsync_NonExistentNode_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        // Act
        var result = await service.GetNodeAsync(TestOrgId, Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_not_found", result.Error);
    }

    [Fact]
    public async Task UpdateNodeAsync_UpdatesDisplayName()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // Act
        var result = await service.UpdateNodeAsync(TestOrgId, node.Id, new UpdateNodeRequest(null, "New Display Name"));

        // Assert
        Assert.True(result.Success);
        Assert.Equal("New Display Name", result.Value!.DisplayName);

        // Verify persisted
        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal("New Display Name", updated!.DisplayName);
    }

    [Fact]
    public async Task UpdateNodeAsync_UpdatesName()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context, name: "old-name");

        // Act
        var result = await service.UpdateNodeAsync(TestOrgId, node.Id, new UpdateNodeRequest("new-name", null));

        // Assert
        Assert.True(result.Success);
        Assert.Equal("new-name", result.Value!.Name);
    }

    [Fact]
    public async Task UpdateNodeAsync_DuplicateName_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        await SeedNodeAsync(context, TestOrgId, name: "existing-name");
        var node = await SeedNodeAsync(context, TestOrgId, name: "my-node");

        // Act
        var result = await service.UpdateNodeAsync(TestOrgId, node.Id, new UpdateNodeRequest("existing-name", null));

        // Assert
        Assert.False(result.Success);
        Assert.Equal("name_already_exists", result.Error);
    }

    [Fact]
    public async Task UpdateNodeAsync_NonExistentNode_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        // Act
        var result = await service.UpdateNodeAsync(TestOrgId, Guid.NewGuid(), new UpdateNodeRequest(null, "Whatever"));

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_not_found", result.Error);
    }

    [Fact]
    public async Task UpdateNodeAsync_ClearsDisplayNameWithEmptyString()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // Act
        var result = await service.UpdateNodeAsync(TestOrgId, node.Id, new UpdateNodeRequest(null, ""));

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Value!.DisplayName);
    }

    [Fact]
    public async Task DecommissionNodeAsync_SetsStatusAndSoftDeletes()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, publisher, _) = CreateService(context, timeProvider);
        var node = await SeedNodeAsync(context, status: NodeStatus.Online);

        // Act
        var result = await service.DecommissionNodeAsync(TestOrgId, node.Id);

        // Assert
        Assert.True(result.Success);

        var decommissioned = await context.Nodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == node.Id);
        Assert.NotNull(decommissioned);
        Assert.Equal(NodeStatus.Decommissioned, decommissioned.Status);
        Assert.Equal(now.UtcDateTime, decommissioned.DeletedAt);
    }

    [Fact]
    public async Task DecommissionNodeAsync_RevokesAllCertificates()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // Add certificates
        context.AgentCertificates.Add(new AgentCertificate
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Thumbprint = "abc123",
            SerialNumber = "001",
            NotBefore = DateTime.UtcNow.AddDays(-30),
            NotAfter = DateTime.UtcNow.AddDays(60),
            IsRevoked = false,
            IssuedAt = DateTime.UtcNow.AddDays(-30)
        });
        context.AgentCertificates.Add(new AgentCertificate
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            Thumbprint = "def456",
            SerialNumber = "002",
            NotBefore = DateTime.UtcNow.AddDays(-10),
            NotAfter = DateTime.UtcNow.AddDays(80),
            IsRevoked = false,
            IssuedAt = DateTime.UtcNow.AddDays(-10)
        });
        await context.SaveChangesAsync();

        // Act
        await service.DecommissionNodeAsync(TestOrgId, node.Id);

        // Assert
        var certs = await context.AgentCertificates
            .Where(c => c.NodeId == node.Id)
            .ToListAsync();
        Assert.All(certs, c =>
        {
            Assert.True(c.IsRevoked);
            Assert.Equal("Node decommissioned", c.RevocationReason);
        });
    }

    [Fact]
    public async Task DecommissionNodeAsync_PublishesEvent()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        // Act
        await service.DecommissionNodeAsync(TestOrgId, node.Id);

        // Assert
        Assert.True(publisher.HasMessage<NodeDecommissioned>());
        var evt = publisher.GetLastMessage<NodeDecommissioned>()!;
        Assert.Equal(node.Id, evt.NodeId);
    }

    [Fact]
    public async Task EnterMaintenanceAsync_SetsMaintenanceStatus()
    {
        // Arrange
        using var context = CreateContext();
        var (service, publisher, _) = CreateService(context);
        var node = await SeedNodeAsync(context, status: NodeStatus.Online);

        // Act
        var result = await service.EnterMaintenanceAsync(TestOrgId, node.Id);

        // Assert
        Assert.True(result.Success);

        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Maintenance, updated!.Status);
        Assert.True(publisher.HasMessage<NodeMaintenanceStarted>());
    }

    [Fact]
    public async Task EnterMaintenanceAsync_AlreadyInMaintenance_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context, status: NodeStatus.Maintenance);

        // Act
        var result = await service.EnterMaintenanceAsync(TestOrgId, node.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("already_in_maintenance", result.Error);
    }

    [Fact]
    public async Task EnterMaintenanceAsync_DecommissionedNode_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        // Add a decommissioned node (without DeletedAt to bypass query filter)
        var decomNode = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = TestOrgId,
            Name = "decom-node",
            Status = NodeStatus.Decommissioned,
            Platform = "linux",
            CreatedAt = DateTime.UtcNow
        };
        context.Nodes.Add(decomNode);
        await context.SaveChangesAsync();

        // Act
        var result = await service.EnterMaintenanceAsync(TestOrgId, decomNode.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_decommissioned", result.Error);
    }

    [Fact]
    public async Task ExitMaintenanceAsync_ReturnsToOnline_WhenRecentHeartbeat()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, publisher, _) = CreateService(context, timeProvider);
        var node = await SeedNodeAsync(context,
            status: NodeStatus.Maintenance,
            lastHeartbeat: now.UtcDateTime.AddMinutes(-2)); // 2 mins ago = recent

        // Act
        var result = await service.ExitMaintenanceAsync(TestOrgId, node.Id);

        // Assert
        Assert.True(result.Success);

        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Online, updated!.Status);
        Assert.True(publisher.HasMessage<NodeMaintenanceEnded>());
    }

    [Fact]
    public async Task ExitMaintenanceAsync_ReturnsToOffline_WhenStaleHeartbeat()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var context = CreateContext();
        var (service, _, _) = CreateService(context, timeProvider);
        var node = await SeedNodeAsync(context,
            status: NodeStatus.Maintenance,
            lastHeartbeat: now.UtcDateTime.AddMinutes(-10)); // 10 mins ago = stale

        // Act
        var result = await service.ExitMaintenanceAsync(TestOrgId, node.Id);

        // Assert
        Assert.True(result.Success);

        var updated = await context.Nodes.FindAsync(node.Id);
        Assert.Equal(NodeStatus.Offline, updated!.Status);
    }

    [Fact]
    public async Task ExitMaintenanceAsync_NotInMaintenance_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context, status: NodeStatus.Online);

        // Act
        var result = await service.ExitMaintenanceAsync(TestOrgId, node.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("not_in_maintenance", result.Error);
    }

    [Fact]
    public async Task ExitMaintenanceAsync_NonExistentNode_ReturnsFail()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        // Act
        var result = await service.ExitMaintenanceAsync(TestOrgId, Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal("node_not_found", result.Error);
    }

    [Fact]
    public async Task GetNodesAsync_ExcludesSoftDeletedNodes()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);

        await SeedNodeAsync(context, TestOrgId, name: "active-node");

        // Add soft-deleted node directly
        var deletedNode = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = TestOrgId,
            Name = "deleted-node",
            Status = NodeStatus.Decommissioned,
            Platform = "linux",
            CreatedAt = DateTime.UtcNow,
            DeletedAt = DateTime.UtcNow // Soft deleted
        };
        context.Nodes.Add(deletedNode);
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetNodesAsync(TestOrgId, new NodeListQuery());

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("active-node", result.Items.First().Name);
    }

    [Fact]
    public async Task GetNodeAsync_IncludesHealthData()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        context.NodeHealths.Add(new NodeHealth
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            CpuUsagePercent = 45.5,
            MemoryUsagePercent = 60.0,
            DiskUsagePercent = 30.0,
            ActiveGameServers = 3,
            ReportedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetNodeAsync(TestOrgId, node.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Value!.Health);
        Assert.Equal(45.5, result.Value.Health.CpuUsagePercent);
        Assert.Equal(3, result.Value.Health.ActiveGameServers);
    }

    [Fact]
    public async Task GetNodeAsync_IncludesCapacityData()
    {
        // Arrange
        using var context = CreateContext();
        var (service, _, _) = CreateService(context);
        var node = await SeedNodeAsync(context);

        context.NodeCapacities.Add(new NodeCapacity
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            MaxGameServers = 10,
            CurrentGameServers = 5,
            AvailableMemoryBytes = 8L * 1024 * 1024 * 1024,
            AvailableDiskBytes = 250L * 1024 * 1024 * 1024,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetNodeAsync(TestOrgId, node.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Value!.Capacity);
        Assert.Equal(10, result.Value.Capacity.MaxGameServers);
        Assert.Equal(5, result.Value.Capacity.CurrentGameServers);
    }
}
