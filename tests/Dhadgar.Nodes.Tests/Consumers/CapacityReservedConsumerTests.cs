using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Consumers;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Tests.TestHelpers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.Consumers;

public sealed class CapacityReservedConsumerTests
{
    private static NodesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new NodesDbContext(options);
    }

    private static IOptions<NodesOptions> CreateOptions(double lowCapacityThreshold = 80.0) =>
        Options.Create(new NodesOptions { LowCapacityThresholdPercent = lowCapacityThreshold });

    private static CapacityReservedConsumer CreateConsumer(
        NodesDbContext context,
        IPublishEndpoint publishEndpoint,
        IOptions<NodesOptions>? options = null,
        TimeProvider? timeProvider = null,
        ILogger<CapacityReservedConsumer>? logger = null)
    {
        return new CapacityReservedConsumer(
            context,
            publishEndpoint,
            options ?? CreateOptions(),
            timeProvider ?? new FakeTimeProvider(DateTimeOffset.UtcNow),
            logger ?? Substitute.For<ILogger<CapacityReservedConsumer>>());
    }

    [Fact]
    public async Task Consume_LogsReservationDetails()
    {
        // Arrange
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var logger = Substitute.For<ILogger<CapacityReservedConsumer>>();
        var consumer = CreateConsumer(context, publishEndpoint, logger: logger);

        var nodeId = Guid.NewGuid();
        var reservationToken = Guid.NewGuid();
        var message = new CapacityReserved(
            NodeId: nodeId,
            ReservationToken: reservationToken,
            MemoryMb: 4096,
            DiskMb: 50000,
            CpuMillicores: 2000,
            ExpiresAt: DateTime.UtcNow.AddMinutes(30),
            RequestedBy: "test-server-deployment");

        var consumeContext = ConsumeContextHelper.CreateConsumeContext(message);

        // Act
        await consumer.Consume(consumeContext);

        // Assert - verify logging occurred
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains(nodeId.ToString())),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Consume_CompletesSuccessfully()
    {
        // Arrange
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var consumer = CreateConsumer(context, publishEndpoint);

        var message = new CapacityReserved(
            NodeId: Guid.NewGuid(),
            ReservationToken: Guid.NewGuid(),
            MemoryMb: 2048,
            DiskMb: 10000,
            CpuMillicores: 1000,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RequestedBy: "test-service");

        var consumeContext = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should not throw
        await consumer.Consume(consumeContext);
    }

    [Fact]
    public async Task Consume_HandlesLargeCapacityValues()
    {
        // Arrange
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var consumer = CreateConsumer(context, publishEndpoint);

        var message = new CapacityReserved(
            NodeId: Guid.NewGuid(),
            ReservationToken: Guid.NewGuid(),
            MemoryMb: 128000, // 128GB
            DiskMb: 2000000, // 2TB
            CpuMillicores: 64000, // 64 cores
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            RequestedBy: "large-deployment");

        var consumeContext = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should handle large values
        await consumer.Consume(consumeContext);
    }

    [Fact]
    public async Task Consume_PublishesLowCapacityAlert_WhenBelowThreshold()
    {
        // Arrange
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var options = CreateOptions(lowCapacityThreshold: 80.0);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var consumer = CreateConsumer(context, publishEndpoint, options, timeProvider);

        // Seed a node with capacity data showing high usage (85% memory used)
        var nodeId = Guid.NewGuid();
        var node = new Node
        {
            Id = nodeId,
            OrganizationId = Guid.NewGuid(),
            Name = "test-node",
            Platform = "linux",
            Status = NodeStatus.Online,
            CreatedAt = DateTime.UtcNow
        };
        context.Nodes.Add(node);

        // Total: 16GB memory, 100GB disk; Available: 2.4GB memory (15% available = 85% used), 50GB disk
        context.HardwareInventories.Add(new NodeHardwareInventory
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Hostname = "test-host",
            MemoryBytes = 16L * 1024 * 1024 * 1024, // 16GB total
            DiskBytes = 100L * 1024 * 1024 * 1024,  // 100GB total
            CpuCores = 8,
            CollectedAt = DateTime.UtcNow
        });
        context.NodeCapacities.Add(new NodeCapacity
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            AvailableMemoryBytes = (long)(16L * 1024 * 1024 * 1024 * 0.15), // 15% available = 85% used
            AvailableDiskBytes = 50L * 1024 * 1024 * 1024,                   // 50% available
            MaxGameServers = 10,
            CurrentGameServers = 8,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var message = new CapacityReserved(
            NodeId: nodeId,
            ReservationToken: Guid.NewGuid(),
            MemoryMb: 1024,
            DiskMb: 5000,
            CpuMillicores: 1000,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RequestedBy: "test-service");

        var consumeContext = ConsumeContextHelper.CreateConsumeContext(message);

        // Act
        await consumer.Consume(consumeContext);

        // Assert - should publish low capacity alert
        await publishEndpoint.Received(1).Publish(
            Arg.Is<NodeCapacityLow>(e =>
                e.NodeId == nodeId &&
                e.MemoryUsagePercent >= 80.0 &&
                e.ThresholdPercent == 80.0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DoesNotPublishAlert_WhenAboveThreshold()
    {
        // Arrange
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var options = CreateOptions(lowCapacityThreshold: 80.0);
        var consumer = CreateConsumer(context, publishEndpoint, options);

        // Seed a node with capacity data showing low usage (50% used)
        var nodeId = Guid.NewGuid();
        var node = new Node
        {
            Id = nodeId,
            OrganizationId = Guid.NewGuid(),
            Name = "test-node",
            Platform = "linux",
            Status = NodeStatus.Online,
            CreatedAt = DateTime.UtcNow
        };
        context.Nodes.Add(node);

        context.HardwareInventories.Add(new NodeHardwareInventory
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Hostname = "test-host",
            MemoryBytes = 16L * 1024 * 1024 * 1024,
            DiskBytes = 100L * 1024 * 1024 * 1024,
            CpuCores = 8,
            CollectedAt = DateTime.UtcNow
        });
        context.NodeCapacities.Add(new NodeCapacity
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            AvailableMemoryBytes = 8L * 1024 * 1024 * 1024,  // 50% available
            AvailableDiskBytes = 50L * 1024 * 1024 * 1024,   // 50% available
            MaxGameServers = 10,
            CurrentGameServers = 5,
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var message = new CapacityReserved(
            NodeId: nodeId,
            ReservationToken: Guid.NewGuid(),
            MemoryMb: 1024,
            DiskMb: 5000,
            CpuMillicores: 1000,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RequestedBy: "test-service");

        var consumeContext = ConsumeContextHelper.CreateConsumeContext(message);

        // Act
        await consumer.Consume(consumeContext);

        // Assert - should NOT publish low capacity alert
        await publishEndpoint.DidNotReceive().Publish(
            Arg.Any<NodeCapacityLow>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DoesNotPublishAlert_WhenNodeNotFound()
    {
        // Arrange
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var consumer = CreateConsumer(context, publishEndpoint);

        var message = new CapacityReserved(
            NodeId: Guid.NewGuid(), // Non-existent node
            ReservationToken: Guid.NewGuid(),
            MemoryMb: 1024,
            DiskMb: 5000,
            CpuMillicores: 1000,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RequestedBy: "test-service");

        var consumeContext = ConsumeContextHelper.CreateConsumeContext(message);

        // Act
        await consumer.Consume(consumeContext);

        // Assert - should NOT publish alert (node not found)
        await publishEndpoint.DidNotReceive().Publish(
            Arg.Any<NodeCapacityLow>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DoesNotPublishAlert_WhenCapacityDataMissing()
    {
        // Arrange
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var consumer = CreateConsumer(context, publishEndpoint);

        // Seed a node without capacity data
        var nodeId = Guid.NewGuid();
        var node = new Node
        {
            Id = nodeId,
            OrganizationId = Guid.NewGuid(),
            Name = "test-node",
            Platform = "linux",
            Status = NodeStatus.Online,
            CreatedAt = DateTime.UtcNow
        };
        context.Nodes.Add(node);
        await context.SaveChangesAsync();

        var message = new CapacityReserved(
            NodeId: nodeId,
            ReservationToken: Guid.NewGuid(),
            MemoryMb: 1024,
            DiskMb: 5000,
            CpuMillicores: 1000,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RequestedBy: "test-service");

        var consumeContext = ConsumeContextHelper.CreateConsumeContext(message);

        // Act
        await consumer.Consume(consumeContext);

        // Assert - should NOT publish alert (missing capacity data)
        await publishEndpoint.DidNotReceive().Publish(
            Arg.Any<NodeCapacityLow>(),
            Arg.Any<CancellationToken>());
    }
}
