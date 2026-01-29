using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Observability;
using MassTransit;

namespace Dhadgar.Nodes.Consumers;

/// <summary>
/// Handles NodeDegraded events by logging the degradation and updating metrics.
/// Could trigger notifications to node owners or publish to monitoring systems.
/// </summary>
public sealed class NodeDegradedConsumer : IConsumer<NodeDegraded>
{
    private readonly ILogger<NodeDegradedConsumer> _logger;

    public NodeDegradedConsumer(ILogger<NodeDegradedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<NodeDegraded> context)
    {
        var message = context.Message;
        var issuesSummary = message.Issues.Count > 0
            ? string.Join(", ", message.Issues)
            : "No specific issues reported";

        _logger.LogWarning(
            "Node {NodeId} entered degraded state at {Timestamp}. Issues: [{Issues}]",
            message.NodeId,
            message.Timestamp,
            issuesSummary);

        // Update metrics for degraded nodes
        NodesMetrics.RecordNodeDegraded();

        // TODO: Integrate with Notifications service to alert node owners
        // This would publish a NotifyNodeOwner command or similar

        // TODO: Consider publishing to external monitoring systems (PagerDuty, etc.)
        // based on severity or issue types

        return Task.CompletedTask;
    }
}
