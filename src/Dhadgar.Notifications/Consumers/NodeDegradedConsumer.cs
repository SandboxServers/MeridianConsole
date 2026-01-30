using System.Diagnostics;
using System.Globalization;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Messaging.Consumers;
using Dhadgar.Notifications.Alerting;
using MassTransit;

namespace Dhadgar.Notifications.Consumers;

/// <summary>
/// Handles NodeDegraded events by dispatching warning alerts to configured channels.
/// </summary>
public sealed class NodeDegradedConsumer : DhadgarConsumer<NodeDegraded>
{
    private readonly IAlertDispatcher _alertDispatcher;

    public NodeDegradedConsumer(
        ILogger<NodeDegradedConsumer> logger,
        IAlertDispatcher alertDispatcher) : base(logger)
    {
        _alertDispatcher = alertDispatcher;
    }

    protected override async Task ConsumeAsync(ConsumeContext<NodeDegraded> context, CancellationToken ct)
    {
        var message = context.Message;

        Logger.LogWarning(
            "Received NodeDegraded event for node {NodeId}. Issues: {Issues}",
            message.NodeId,
            string.Join(", ", message.Issues ?? Array.Empty<string>()));

        var alert = new AlertMessage
        {
            Title = "Node Degraded Alert",
            Message = $"Node {message.NodeId} is degraded. Issues: {string.Join(", ", message.Issues ?? Array.Empty<string>())}",
            Severity = AlertSeverity.Warning,
            ServiceName = "Nodes",
            Timestamp = new DateTimeOffset(message.Timestamp, TimeSpan.Zero),
            CorrelationId = context.CorrelationId?.ToString(),
            TraceId = Activity.Current?.TraceId.ToString(),
            AdditionalData = new Dictionary<string, string>
            {
                ["NodeId"] = message.NodeId.ToString(),
                ["IssueCount"] = (message.Issues?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
            }
        };

        await _alertDispatcher.DispatchAsync(alert, ct);
    }
}
