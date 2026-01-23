using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Observability;
using MassTransit;

namespace Dhadgar.Nodes.Consumers;

/// <summary>
/// Handles CapacityReservationExpired events by logging expired reservations.
/// Expired reservations may indicate deployment failures or abandoned workflows.
/// </summary>
public sealed class CapacityReservationExpiredConsumer : IConsumer<CapacityReservationExpired>
{
    private readonly ILogger<CapacityReservationExpiredConsumer> _logger;

    public CapacityReservationExpiredConsumer(ILogger<CapacityReservationExpiredConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<CapacityReservationExpired> context)
    {
        var message = context.Message;

        // Log at Warning level since expired reservations may indicate issues
        _logger.LogWarning(
            "Capacity reservation expired on node {NodeId}: Token {ReservationToken}, ExpiredAt: {ExpiredAt}. " +
            "This may indicate a failed or abandoned deployment workflow.",
            message.NodeId,
            message.ReservationToken,
            message.ExpiredAt);

        // Update metrics for expired reservations
        NodesMetrics.RecordCapacityExpiration(message.NodeId);

        // TODO: Consider triggering alerts for operations team if expiration rate is high
        // This could integrate with Notifications service or external alerting systems

        return Task.CompletedTask;
    }
}
