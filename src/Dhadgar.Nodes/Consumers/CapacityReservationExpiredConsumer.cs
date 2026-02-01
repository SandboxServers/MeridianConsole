using System.Security.Cryptography;
using System.Text;
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
        var maskedToken = RedactToken(message.ReservationToken.ToString());

        // Log at Warning level since expired reservations may indicate issues
        _logger.LogWarning(
            "Capacity reservation expired on node {NodeId}: Token {ReservationToken}, ExpiredAt: {ExpiredAt}. " +
            "This may indicate a failed or abandoned deployment workflow.",
            message.NodeId,
            maskedToken,
            message.ExpiredAt);

        // Update metrics for expired reservations
        NodesMetrics.RecordCapacityExpiration();

        // TODO: Consider triggering alerts for operations team if expiration rate is high
        // This could integrate with Notifications service or external alerting systems

        return Task.CompletedTask;
    }

    /// <summary>
    /// Redacts a reservation token for safe logging by showing only the first few characters
    /// followed by a short hash suffix for correlation purposes.
    /// </summary>
    private static string RedactToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "[empty]";
        }

        if (token.Length <= 4)
        {
            return "****";
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var hashSuffix = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
        return $"{token[..4]}...{hashSuffix}";
    }
}
