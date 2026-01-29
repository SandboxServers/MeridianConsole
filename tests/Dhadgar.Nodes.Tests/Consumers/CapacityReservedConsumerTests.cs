using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Consumers;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.Consumers;

public sealed class CapacityReservedConsumerTests
{
    private readonly ILogger<CapacityReservedConsumer> _logger;
    private readonly CapacityReservedConsumer _consumer;

    public CapacityReservedConsumerTests()
    {
        _logger = Substitute.For<ILogger<CapacityReservedConsumer>>();
        _consumer = new CapacityReservedConsumer(_logger);
    }

    [Fact]
    public async Task Consume_LogsReservationDetails()
    {
        // Arrange
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

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context);

        // Assert - verify logging occurred
        _logger.Received().Log(
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
        var message = new CapacityReserved(
            NodeId: Guid.NewGuid(),
            ReservationToken: Guid.NewGuid(),
            MemoryMb: 2048,
            DiskMb: 10000,
            CpuMillicores: 1000,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            RequestedBy: "test-service");

        var context = CreateConsumeContext(message);

        // Act & Assert - should not throw
        await _consumer.Consume(context);
    }

    [Fact]
    public async Task Consume_HandlesLargeCapacityValues()
    {
        // Arrange
        var message = new CapacityReserved(
            NodeId: Guid.NewGuid(),
            ReservationToken: Guid.NewGuid(),
            MemoryMb: 128000, // 128GB
            DiskMb: 2000000, // 2TB
            CpuMillicores: 64000, // 64 cores
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            RequestedBy: "large-deployment");

        var context = CreateConsumeContext(message);

        // Act & Assert - should handle large values
        await _consumer.Consume(context);
    }

    private static ConsumeContext<CapacityReserved> CreateConsumeContext(CapacityReserved message)
    {
        var context = Substitute.For<ConsumeContext<CapacityReserved>>();
        context.Message.Returns(message);
        return context;
    }
}
