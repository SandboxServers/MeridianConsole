using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Consumers;
using Dhadgar.Nodes.Tests.TestHelpers;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.Consumers;

public sealed class CapacityReservationExpiredConsumerTests
{
    private readonly ILogger<CapacityReservationExpiredConsumer> _logger;
    private readonly CapacityReservationExpiredConsumer _consumer;

    public CapacityReservationExpiredConsumerTests()
    {
        _logger = Substitute.For<ILogger<CapacityReservationExpiredConsumer>>();
        _consumer = new CapacityReservationExpiredConsumer(_logger);
    }

    [Fact]
    public async Task Consume_LogsExpirationAtWarningLevel()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var reservationToken = Guid.NewGuid();
        var expiredAt = DateTime.UtcNow;
        var message = new CapacityReservationExpired(
            NodeId: nodeId,
            ReservationToken: reservationToken,
            ExpiredAt: expiredAt);

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context);

        // Assert - verify Warning level logging occurred (expired reservations may indicate issues)
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains(nodeId.ToString())),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Consume_CompletesSuccessfully()
    {
        // Arrange
        var message = new CapacityReservationExpired(
            NodeId: Guid.NewGuid(),
            ReservationToken: Guid.NewGuid(),
            ExpiredAt: DateTime.UtcNow);

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should not throw
        await _consumer.Consume(context);
    }

    [Fact]
    public async Task Consume_HandlesOldExpiration()
    {
        // Arrange - expiration that happened long ago
        var message = new CapacityReservationExpired(
            NodeId: Guid.NewGuid(),
            ReservationToken: Guid.NewGuid(),
            ExpiredAt: DateTime.UtcNow.AddDays(-7));

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should handle old timestamps
        await _consumer.Consume(context);
    }
}
