using Dhadgar.Nodes.Audit;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Nodes.Tests;

public sealed class AuditServiceTests
{
    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new NodesDbContext(options);
    }

    private static TestAuditContextAccessor CreateMockContextAccessor(
        string actorId = "test-user",
        ActorType actorType = ActorType.User,
        string? correlationId = "test-correlation-id",
        string? requestId = "test-request-id",
        string? ipAddress = "192.168.1.1",
        string? userAgent = "TestAgent/1.0")
    {
        return new TestAuditContextAccessor(actorId, actorType, correlationId, requestId, ipAddress, userAgent);
    }

    private sealed class TestAuditContextAccessor : IAuditContextAccessor
    {
        private readonly string _actorId;
        private readonly ActorType _actorType;
        private readonly string? _correlationId;
        private readonly string? _requestId;
        private readonly string? _ipAddress;
        private readonly string? _userAgent;

        public TestAuditContextAccessor(
            string actorId,
            ActorType actorType,
            string? correlationId,
            string? requestId,
            string? ipAddress,
            string? userAgent)
        {
            _actorId = actorId;
            _actorType = actorType;
            _correlationId = correlationId;
            _requestId = requestId;
            _ipAddress = ipAddress;
            _userAgent = userAgent;
        }

        public string GetActorId() => _actorId;
        public ActorType GetActorType() => _actorType;
        public string? GetCorrelationId() => _correlationId;
        public string? GetRequestId() => _requestId;
        public string? GetIpAddress() => _ipAddress;
        public string? GetUserAgent() => _userAgent;
    }

    [Fact]
    public async Task LogAsync_Entry_CreatesAuditLogInDatabase()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        var entry = new AuditEntry
        {
            Action = AuditActions.NodeCreated,
            ResourceType = ResourceTypes.Node,
            ResourceId = Guid.NewGuid(),
            ResourceName = "test-node",
            OrganizationId = Guid.NewGuid(),
            Outcome = AuditOutcome.Success,
            Details = new { TestProperty = "TestValue" }
        };

        // Act
        await service.LogAsync(entry);

        // Assert
        var auditLog = await context.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(auditLog);
        Assert.Equal(entry.Action, auditLog.Action);
        Assert.Equal(entry.ResourceType, auditLog.ResourceType);
        Assert.Equal(entry.ResourceId, auditLog.ResourceId);
        Assert.Equal(entry.ResourceName, auditLog.ResourceName);
        Assert.Equal(entry.OrganizationId, auditLog.OrganizationId);
        Assert.Equal(AuditOutcome.Success, auditLog.Outcome);
        Assert.Equal("test-user", auditLog.ActorId);
        Assert.Equal(ActorType.User, auditLog.ActorType);
        Assert.Equal("test-correlation-id", auditLog.CorrelationId);
        Assert.Equal("test-request-id", auditLog.RequestId);
        Assert.Equal("192.168.1.1", auditLog.IpAddress);
        Assert.Equal("TestAgent/1.0", auditLog.UserAgent);
        Assert.Contains("TestValue", auditLog.Details!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogAsync_Convenience_CreatesAuditLogInDatabase()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        var nodeId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        // Act
        await service.LogAsync(
            AuditActions.NodeDecommissioned,
            ResourceTypes.Node,
            nodeId,
            AuditOutcome.Success,
            new { Reason = "Testing" },
            resourceName: "test-node",
            organizationId: orgId);

        // Assert
        var auditLog = await context.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(auditLog);
        Assert.Equal(AuditActions.NodeDecommissioned, auditLog.Action);
        Assert.Equal(nodeId, auditLog.ResourceId);
        Assert.Equal(orgId, auditLog.OrganizationId);
    }

    [Fact]
    public async Task LogAsync_FailureOutcome_SetsFailureReason()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        // Act
        await service.LogAsync(
            AuditActions.EnrollmentFailed,
            ResourceTypes.Node,
            null,
            AuditOutcome.Failure,
            failureReason: "invalid_token");

        // Assert
        var auditLog = await context.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(auditLog);
        Assert.Equal(AuditOutcome.Failure, auditLog.Outcome);
        Assert.Equal("invalid_token", auditLog.FailureReason);
    }

    [Fact]
    public async Task LogAsync_WithActorOverride_UsesOverrideValues()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(actorId: "original-actor", actorType: ActorType.User),
            timeProvider,
            NullLogger<AuditService>.Instance);

        var entry = new AuditEntry
        {
            Action = AuditActions.HeartbeatReceived,
            ResourceType = ResourceTypes.Node,
            ResourceId = Guid.NewGuid(),
            Outcome = AuditOutcome.Success,
            ActorIdOverride = "agent:node-123",
            ActorTypeOverride = ActorType.Agent
        };

        // Act
        await service.LogAsync(entry);

        // Assert
        var auditLog = await context.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(auditLog);
        Assert.Equal("agent:node-123", auditLog.ActorId);
        Assert.Equal(ActorType.Agent, auditLog.ActorType);
    }

    [Fact]
    public async Task QueryAsync_FiltersByOrganization()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        var org1 = Guid.NewGuid();
        var org2 = Guid.NewGuid();

        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success, organizationId: org1);
        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success, organizationId: org1);
        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success, organizationId: org2);

        // Act
        var result = await service.QueryAsync(new AuditQuery { OrganizationId = org1 });

        // Assert
        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal(org1, item.OrganizationId));
    }

    [Fact]
    public async Task QueryAsync_FiltersByAction()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);
        await service.LogAsync(AuditActions.NodeUpdated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);
        await service.LogAsync(AuditActions.NodeDecommissioned, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);

        // Act
        var result = await service.QueryAsync(new AuditQuery { Action = AuditActions.NodeUpdated });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(AuditActions.NodeUpdated, result.Items.First().Action);
    }

    [Fact]
    public async Task QueryAsync_FiltersByActionWildcard()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);
        await service.LogAsync(AuditActions.NodeUpdated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);
        await service.LogAsync(AuditActions.EnrollmentCompleted, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);
        await service.LogAsync(AuditActions.CertificateIssued, ResourceTypes.Certificate, Guid.NewGuid(), AuditOutcome.Success);

        // Act - filter using wildcard "node.*"
        var result = await service.QueryAsync(new AuditQuery { Action = "node.*" });

        // Assert
        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.StartsWith("node.", item.Action, StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryAsync_FiltersByOutcome()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        await service.LogAsync(AuditActions.EnrollmentCompleted, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);
        await service.LogAsync(AuditActions.EnrollmentFailed, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Failure, failureReason: "test");
        await service.LogAsync(AuditActions.AccessDenied, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Denied);

        // Act
        var result = await service.QueryAsync(new AuditQuery { Outcome = AuditOutcome.Failure });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Failure", result.Items.First().Outcome);
    }

    [Fact]
    public async Task QueryAsync_FiltersByDateRange()
    {
        // Arrange
        using var context = CreateContext();
        var startDate = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(startDate);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        // Create entries at different times
        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);

        timeProvider.Advance(TimeSpan.FromDays(1));
        await service.LogAsync(AuditActions.NodeUpdated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);

        timeProvider.Advance(TimeSpan.FromDays(1));
        await service.LogAsync(AuditActions.NodeDecommissioned, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);

        // Act - filter for middle day using full day range for robustness
        var result = await service.QueryAsync(new AuditQuery
        {
            StartDate = startDate.UtcDateTime.AddDays(1),
            EndDate = startDate.UtcDateTime.AddDays(2).AddTicks(-1)
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(AuditActions.NodeUpdated, result.Items.First().Action);
    }

    [Fact]
    public async Task QueryAsync_SupportsPagination()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        // Create 15 entries
        for (int i = 0; i < 15; i++)
        {
            await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);
        }

        // Act
        var page1 = await service.QueryAsync(new AuditQuery { Page = 1, PageSize = 5 });
        var page2 = await service.QueryAsync(new AuditQuery { Page = 2, PageSize = 5 });
        var page3 = await service.QueryAsync(new AuditQuery { Page = 3, PageSize = 5 });

        // Assert
        Assert.Equal(15, page1.Total);
        Assert.Equal(5, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal(5, page3.Items.Count);
        Assert.False(page3.HasNext); // Page 3 should not have next
    }

    [Fact]
    public async Task QueryAsync_FiltersByResourceId()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        var targetId = Guid.NewGuid();
        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, targetId, AuditOutcome.Success);
        await service.LogAsync(AuditActions.NodeUpdated, ResourceTypes.Node, targetId, AuditOutcome.Success);
        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);

        // Act
        var result = await service.QueryAsync(new AuditQuery { ResourceId = targetId });

        // Assert
        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal(targetId, item.ResourceId));
    }

    [Fact]
    public async Task QueryAsync_FiltersByCorrelationId()
    {
        // Arrange
        using var context = CreateContext();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        // Create two services with different correlation IDs
        var service1 = new AuditService(
            context,
            CreateMockContextAccessor(correlationId: "corr-1"),
            timeProvider,
            NullLogger<AuditService>.Instance);

        var service2 = new AuditService(
            context,
            CreateMockContextAccessor(correlationId: "corr-2"),
            timeProvider,
            NullLogger<AuditService>.Instance);

        await service1.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);
        await service2.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success);

        // Act
        var result = await service1.QueryAsync(new AuditQuery { CorrelationId = "corr-1" });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("corr-1", result.Items.First().CorrelationId);
    }

    [Fact]
    public async Task QueryAsync_OrdersByTimestampDescending()
    {
        // Arrange
        using var context = CreateContext();
        var startDate = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(startDate);
        var service = new AuditService(
            context,
            CreateMockContextAccessor(),
            timeProvider,
            NullLogger<AuditService>.Instance);

        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success, resourceName: "first");
        timeProvider.Advance(TimeSpan.FromHours(1));
        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success, resourceName: "second");
        timeProvider.Advance(TimeSpan.FromHours(1));
        await service.LogAsync(AuditActions.NodeCreated, ResourceTypes.Node, Guid.NewGuid(), AuditOutcome.Success, resourceName: "third");

        // Act
        var result = await service.QueryAsync(new AuditQuery());

        // Assert - should be in reverse order (newest first)
        var items = result.Items.ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal("third", items[0].ResourceName);
        Assert.Equal("second", items[1].ResourceName);
        Assert.Equal("first", items[2].ResourceName);
    }
}
