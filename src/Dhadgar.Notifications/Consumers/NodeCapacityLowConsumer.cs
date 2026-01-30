using Dhadgar.Contracts.Nodes;
using Dhadgar.Messaging.Consumers;
using Dhadgar.Notifications.Alerting;
using MassTransit;

namespace Dhadgar.Notifications.Consumers;

/// <summary>
/// Handles NodeCapacityLow events by dispatching warning alerts about low node resources.
/// </summary>
public sealed class NodeCapacityLowConsumer : DhadgarConsumer<NodeCapacityLow>
{
    private readonly IAlertDispatcher _alertDispatcher;

    public NodeCapacityLowConsumer(
        ILogger<NodeCapacityLowConsumer> logger,
        IAlertDispatcher alertDispatcher) : base(logger)
    {
        _alertDispatcher = alertDispatcher;
    }

    protected override async Task ConsumeAsync(ConsumeContext<NodeCapacityLow> context, CancellationToken ct)
    {
        var message = context.Message;

        Logger.LogWarning(
            "Received NodeCapacityLow event for node {NodeId}. Memory: {MemoryPercent:F1}%, Disk: {DiskPercent:F1}%",
            message.NodeId,
            message.MemoryUsagePercent,
            message.DiskUsagePercent);

        var alert = new AlertMessage
        {
            Title = "Node Capacity Low Alert",
            Message = $"Node {message.NodeId} is running low on resources. " +
                      $"Memory: {message.MemoryUsagePercent:F1}% used, Disk: {message.DiskUsagePercent:F1}% used",
            Severity = AlertSeverity.Warning,
            ServiceName = "Nodes",
            Timestamp = new DateTimeOffset(message.Timestamp, TimeSpan.Zero),
            AdditionalData = new Dictionary<string, string>
            {
                ["NodeId"] = message.NodeId.ToString(),
                ["MemoryUsagePercent"] = message.MemoryUsagePercent.ToString("F1"),
                ["DiskUsagePercent"] = message.DiskUsagePercent.ToString("F1"),
                ["ThresholdPercent"] = message.ThresholdPercent.ToString("F1")
            }
        };

        await _alertDispatcher.DispatchAsync(alert, ct);
    }
}
