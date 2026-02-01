using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Consumers;

/// <summary>
/// Handles CapacityReserved events by logging the reservation and updating metrics.
/// Triggers alerts if capacity is running low on a node.
/// </summary>
public sealed class CapacityReservedConsumer : IConsumer<CapacityReserved>
{
    private readonly NodesDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CapacityReservedConsumer> _logger;
    private readonly NodesOptions _options;
    private readonly TimeProvider _timeProvider;

    public CapacityReservedConsumer(
        NodesDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        IOptions<NodesOptions> options,
        TimeProvider timeProvider,
        ILogger<CapacityReservedConsumer> logger)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CapacityReserved> context)
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

        // Check remaining capacity and alert if running low
        await CheckAndAlertLowCapacityAsync(message.NodeId, context.CancellationToken);
    }

    private async Task CheckAndAlertLowCapacityAsync(Guid nodeId, CancellationToken ct)
    {
        // Query the node with its capacity and hardware inventory
        var node = await _dbContext.Nodes
            .AsNoTracking()
            .Include(n => n.Capacity)
            .Include(n => n.HardwareInventory)
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node?.Capacity is null || node.HardwareInventory is null)
        {
            _logger.LogDebug(
                "Cannot check capacity for node {NodeId}: missing capacity or hardware inventory data",
                nodeId);
            return;
        }

        var capacity = node.Capacity;
        var hardware = node.HardwareInventory;

        // Calculate usage percentages
        // AvailableMemoryBytes/AvailableDiskBytes represent remaining capacity
        // TotalMemoryBytes/TotalDiskBytes from HardwareInventory represent total capacity
        var memoryUsagePercent = hardware.MemoryBytes > 0
            ? (1.0 - (double)capacity.AvailableMemoryBytes / hardware.MemoryBytes) * 100.0
            : 0.0;

        var diskUsagePercent = hardware.DiskBytes > 0
            ? (1.0 - (double)capacity.AvailableDiskBytes / hardware.DiskBytes) * 100.0
            : 0.0;

        var threshold = _options.LowCapacityThresholdPercent;

        // Check if either memory or disk usage exceeds the threshold
        if (memoryUsagePercent >= threshold || diskUsagePercent >= threshold)
        {
            _logger.LogWarning(
                "Node {NodeId} capacity is low. Memory: {MemoryUsage:F1}% used ({AvailableMemoryMb}MB available of {TotalMemoryMb}MB), " +
                "Disk: {DiskUsage:F1}% used ({AvailableDiskMb}MB available of {TotalDiskMb}MB). Threshold: {Threshold:F1}%",
                nodeId,
                memoryUsagePercent,
                capacity.AvailableMemoryBytes / (1024 * 1024),
                hardware.MemoryBytes / (1024 * 1024),
                diskUsagePercent,
                capacity.AvailableDiskBytes / (1024 * 1024),
                hardware.DiskBytes / (1024 * 1024),
                threshold);

            var alertEvent = new NodeCapacityLow(
                NodeId: nodeId,
                Timestamp: _timeProvider.GetUtcNow().UtcDateTime,
                AvailableMemoryBytes: capacity.AvailableMemoryBytes,
                TotalMemoryBytes: hardware.MemoryBytes,
                AvailableDiskBytes: capacity.AvailableDiskBytes,
                TotalDiskBytes: hardware.DiskBytes,
                MemoryUsagePercent: memoryUsagePercent,
                DiskUsagePercent: diskUsagePercent,
                ThresholdPercent: threshold);

            await _publishEndpoint.Publish(alertEvent, ct);

            _logger.LogDebug(
                "Published NodeCapacityLow alert for node {NodeId}",
                nodeId);
        }
    }
}
