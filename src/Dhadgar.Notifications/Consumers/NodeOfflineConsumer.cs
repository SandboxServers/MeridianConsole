using Dhadgar.Contracts.Nodes;
using Dhadgar.Messaging.Consumers;
using Dhadgar.Notifications.Alerting;
using MassTransit;

namespace Dhadgar.Notifications.Consumers;

/// <summary>
/// Handles NodeOffline events by dispatching high-priority alerts to configured channels.
/// </summary>
public sealed class NodeOfflineConsumer : DhadgarConsumer<NodeOffline>
{
    private readonly IAlertDispatcher _alertDispatcher;

    public NodeOfflineConsumer(
        ILogger<NodeOfflineConsumer> logger,
        IAlertDispatcher alertDispatcher) : base(logger)
    {
        _alertDispatcher = alertDispatcher;
    }

    protected override async Task ConsumeAsync(ConsumeContext<NodeOffline> context, CancellationToken ct)
    {
        var message = context.Message;

        Logger.LogWarning(
            "Received NodeOffline event for node {NodeId}. Reason: {Reason}",
            message.NodeId,
            message.Reason ?? "Unknown");

        var alert = new AlertMessage
        {
            Title = "Node Offline Alert",
            Message = $"Node {message.NodeId} went offline at {message.Timestamp:u}. Reason: {message.Reason ?? "Unknown"}",
            Severity = AlertSeverity.Critical,
            ServiceName = "Nodes",
            Timestamp = new DateTimeOffset(message.Timestamp, TimeSpan.Zero),
            AdditionalData = new Dictionary<string, string>
            {
                ["NodeId"] = message.NodeId.ToString(),
                ["Reason"] = message.Reason ?? "Unknown"
            }
        };

        await _alertDispatcher.DispatchAsync(alert, ct);
    }
}
