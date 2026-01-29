using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Consumers;
using Dhadgar.Nodes.Tests.TestHelpers;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.Consumers;

public sealed class CapacityReleasedConsumerTests
{
    private readonly ILogger<CapacityReleasedConsumer> _logger;
    private readonly CapacityReleasedConsumer _consumer;

    public CapacityReleasedConsumerTests()
    {
        _logger = Substitute.For<ILogger<CapacityReleasedConsumer>>();
        _consumer = new CapacityReleasedConsumer(_logger);
    }

    [Fact]
    public async Task Consume_LogsReleaseDetails()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var reservationToken = Guid.NewGuid();
        var message = new CapacityReleased(
            NodeId: nodeId,
            ReservationToken: reservationToken,
            Reason: "Server deployment cancelled");

        var context = ConsumeContextHelper.CreateConsumeContext(message);

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
        var message = new CapacityReleased(
            NodeId: Guid.NewGuid(),
            ReservationToken: Guid.NewGuid(),
            Reason: "User cancelled");

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should not throw
        await _consumer.Consume(context);
    }

    [Theory]
    [InlineData("Server deployment cancelled")]
    [InlineData("Timeout reached")]
    [InlineData("Manual release by admin")]
    [InlineData("")]
    public async Task Consume_HandlesVariousReasons(string reason)
    {
        // Arrange
        var message = new CapacityReleased(
            NodeId: Guid.NewGuid(),
            ReservationToken: Guid.NewGuid(),
            Reason: reason);

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should handle all reason types
        await _consumer.Consume(context);
    }
}
