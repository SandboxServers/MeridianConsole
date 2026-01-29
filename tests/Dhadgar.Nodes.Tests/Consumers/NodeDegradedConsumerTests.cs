using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Consumers;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dhadgar.Nodes.Tests.Consumers;

public sealed class NodeDegradedConsumerTests
{
    private readonly ILogger<NodeDegradedConsumer> _logger;
    private readonly NodeDegradedConsumer _consumer;

    public NodeDegradedConsumerTests()
    {
        _logger = Substitute.For<ILogger<NodeDegradedConsumer>>();
        _consumer = new NodeDegradedConsumer(_logger);
    }

    [Fact]
    public async Task Consume_LogsDegradationAtWarningLevel()
    {
        // Arrange
        var nodeId = Guid.NewGuid();
        var message = new NodeDegraded(
            NodeId: nodeId,
            Timestamp: DateTime.UtcNow,
            Issues: ["High CPU usage", "Memory pressure"]);

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context);

        // Assert - verify Warning level logging occurred
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
        var message = new NodeDegraded(
            NodeId: Guid.NewGuid(),
            Timestamp: DateTime.UtcNow,
            Issues: ["Test issue"]);

        var context = CreateConsumeContext(message);

        // Act & Assert - should not throw
        await _consumer.Consume(context);
    }

    [Fact]
    public async Task Consume_HandlesMultipleIssues()
    {
        // Arrange
        var issues = new List<string>
        {
            "High CPU usage (95%)",
            "Memory pressure (92%)",
            "Disk almost full (88%)",
            "Network latency detected",
            "Process count exceeded threshold"
        };

        var message = new NodeDegraded(
            NodeId: Guid.NewGuid(),
            Timestamp: DateTime.UtcNow,
            Issues: issues);

        var context = CreateConsumeContext(message);

        // Act & Assert - should handle multiple issues
        await _consumer.Consume(context);
    }

    [Fact]
    public async Task Consume_HandlesEmptyIssuesList()
    {
        // Arrange
        var message = new NodeDegraded(
            NodeId: Guid.NewGuid(),
            Timestamp: DateTime.UtcNow,
            Issues: []);

        var context = CreateConsumeContext(message);

        // Act & Assert - should handle empty issues
        await _consumer.Consume(context);
    }

    [Fact]
    public async Task Consume_LogsIssuesSummary()
    {
        // Arrange
        var issues = new List<string> { "Issue A", "Issue B" };
        var message = new NodeDegraded(
            NodeId: Guid.NewGuid(),
            Timestamp: DateTime.UtcNow,
            Issues: issues);

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context);

        // Assert - verify issues are logged
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Issue A") && o.ToString()!.Contains("Issue B")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    private static ConsumeContext<NodeDegraded> CreateConsumeContext(NodeDegraded message)
    {
        var context = Substitute.For<ConsumeContext<NodeDegraded>>();
        context.Message.Returns(message);
        return context;
    }
}
