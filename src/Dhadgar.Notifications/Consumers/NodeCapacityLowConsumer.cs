using System.Diagnostics;
using System.Globalization;
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
                      $"Memory: {message.MemoryUsagePercent.ToString("F1", CultureInfo.InvariantCulture)}% used, Disk: {message.DiskUsagePercent.ToString("F1", CultureInfo.InvariantCulture)}% used",
            Severity = AlertSeverity.Warning,
            ServiceName = "Nodes",
            Timestamp = new DateTimeOffset(message.Timestamp, TimeSpan.Zero),
            CorrelationId = context.CorrelationId?.ToString(),
            TraceId = Activity.Current?.TraceId.ToString(),
            AdditionalData = new Dictionary<string, string>
            {
                ["NodeId"] = message.NodeId.ToString(),
                ["MemoryUsagePercent"] = message.MemoryUsagePercent.ToString("F1", CultureInfo.InvariantCulture),
                ["DiskUsagePercent"] = message.DiskUsagePercent.ToString("F1", CultureInfo.InvariantCulture),
                ["ThresholdPercent"] = message.ThresholdPercent.ToString("F1", CultureInfo.InvariantCulture)
            }
        };

        await _alertDispatcher.DispatchAsync(alert, ct);
    }
}
