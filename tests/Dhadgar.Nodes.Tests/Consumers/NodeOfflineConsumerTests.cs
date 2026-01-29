using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Consumers;
using Dhadgar.Nodes.Tests.TestHelpers;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.Consumers;

public sealed class NodeOfflineConsumerTests
{
    private readonly ILogger<NodeOfflineConsumer> _logger;
    private readonly NodeOfflineConsumer _consumer;

    public NodeOfflineConsumerTests()
    {
        _logger = Substitute.For<ILogger<NodeOfflineConsumer>>();
        _consumer = new NodeOfflineConsumer(_logger);
    }

    [Fact]
    public async Task Consume_LogsOfflineStatusAtWarningLevel()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var message = new NodeOffline(
            NodeId: nodeId,
            Timestamp: DateTime.UtcNow,
            Reason: "Heartbeat timeout");

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context);

        // Assert - verify Warning level logging occurred (offline is critical)
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
        var message = new NodeOffline(
            NodeId: Guid.NewGuid(),
            Timestamp: DateTime.UtcNow,
            Reason: "Connection lost");

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should not throw
        await _consumer.Consume(context);
    }

    [Fact]
    public async Task Consume_HandlesNullReason()
    {
        // Arrange
        var message = new NodeOffline(
            NodeId: Guid.NewGuid(),
            Timestamp: DateTime.UtcNow,
            Reason: null);

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should handle null reason gracefully
        await _consumer.Consume(context);
    }

    [Theory]
    [InlineData("Heartbeat timeout")]
    [InlineData("Agent shutdown")]
    [InlineData("Network failure")]
    [InlineData("User requested")]
    [InlineData("")]
    public async Task Consume_HandlesVariousReasons(string? reason)
    {
        // Arrange
        var message = new NodeOffline(
            NodeId: Guid.NewGuid(),
            Timestamp: DateTime.UtcNow,
            Reason: reason);

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act & Assert - should handle all reason types
        await _consumer.Consume(context);
    }

    [Fact]
    public async Task Consume_LogsReasonInMessage()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        const string reason = "Heartbeat timeout";
        var message = new NodeOffline(
            NodeId: nodeId,
            Timestamp: DateTime.UtcNow,
            Reason: reason);

        var context = ConsumeContextHelper.CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context);

        // Assert - verify reason is included in log
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains(reason)),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
