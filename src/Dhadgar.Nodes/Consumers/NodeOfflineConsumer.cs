using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Observability;
using MassTransit;

namespace Dhadgar.Nodes.Consumers;

/// <summary>
/// Handles NodeOffline events by logging the offline status and updating metrics.
/// Could trigger alerts or initiate server migration workflows.
/// </summary>
public sealed class NodeOfflineConsumer : IConsumer<NodeOffline>
{
    private readonly ILogger<NodeOfflineConsumer> _logger;

    public NodeOfflineConsumer(ILogger<NodeOfflineConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<NodeOffline> context)
    {
        var message = context.Message;
        var reason = message.Reason ?? "Unknown";

        _logger.LogWarning(
            "Node {NodeId} went offline at {Timestamp}. Reason: {Reason}",
            message.NodeId,
            message.Timestamp,
            reason);

        // Update metrics for offline nodes
        NodesMetrics.RecordNodeOffline();

        // TODO: Trigger high-priority alerts for operations team
        // Node offline events are critical and may affect running game servers

        // TODO: Consider initiating server migration workflows
        // This would query the Servers service for active servers on this node
        // and potentially publish MigrateServer commands to move them

        // TODO: Integrate with Notifications service for node owner alerts
        // This is especially important if the node has active game servers

        return Task.CompletedTask;
    }
}
