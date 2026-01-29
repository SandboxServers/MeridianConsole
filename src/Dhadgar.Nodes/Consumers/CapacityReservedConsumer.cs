using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Observability;
using MassTransit;

namespace Dhadgar.Nodes.Consumers;

/// <summary>
/// Handles CapacityReserved events by logging the reservation and updating metrics.
/// Could trigger alerts if capacity is running low on a node.
/// </summary>
public sealed class CapacityReservedConsumer : IConsumer<CapacityReserved>
{
    private readonly ILogger<CapacityReservedConsumer> _logger;

    public CapacityReservedConsumer(ILogger<CapacityReservedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<CapacityReserved> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Capacity reserved on node {NodeId}: {MemoryMb}MB memory, {DiskMb}MB disk, {CpuMillicores}m CPU. " +
            "Token: {ReservationToken}, Expires: {ExpiresAt}, RequestedBy: {RequestedBy}",
            message.NodeId,
            message.MemoryMb,
            message.DiskMb,
            message.CpuMillicores,
            message.ReservationToken,
            message.ExpiresAt,
            message.RequestedBy);

        // Update metrics for capacity reservations
        NodesMetrics.RecordCapacityReservation();

        // TODO: Check remaining capacity and alert if running low
        // This would require querying the database for current node capacity
        // and comparing against total available. For now, we just log.

        return Task.CompletedTask;
    }
}
