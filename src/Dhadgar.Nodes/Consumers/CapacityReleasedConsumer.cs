using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Observability;
using MassTransit;

namespace Dhadgar.Nodes.Consumers;

/// <summary>
/// Handles CapacityReleased events by logging the release and updating metrics.
/// Useful for monitoring dashboards and capacity tracking.
/// </summary>
public sealed class CapacityReleasedConsumer : IConsumer<CapacityReleased>
{
    private readonly ILogger<CapacityReleasedConsumer> _logger;

    public CapacityReleasedConsumer(ILogger<CapacityReleasedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<CapacityReleased> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Capacity released on node {NodeId}: Token {ReservationToken}, Reason: {Reason}",
            message.NodeId,
            message.ReservationToken,
            message.Reason);

        // Update metrics for capacity releases
        NodesMetrics.RecordCapacityRelease(message.NodeId);

        return Task.CompletedTask;
    }
}
