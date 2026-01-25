using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Models;
using Dhadgar.Nodes.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Nodes.Services;

public sealed class CapacityReservationService : ICapacityReservationService
{
    private readonly NodesDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CapacityReservationService> _logger;

    public CapacityReservationService(
        NodesDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        ILogger<CapacityReservationService> logger)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ServiceResult<ReservationResponse>> ReserveAsync(
        Guid nodeId,
        int memoryMb,
        int diskMb,
        int cpuMillicores,
        string requestedBy,
        int ttlMinutes,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        // Validate node exists and is in a valid state for reservations
        var node = await _dbContext.Nodes
            .Include(n => n.Capacity)
            .Include(n => n.HardwareInventory)
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<ReservationResponse>("node_not_found");
        }

        if (node.Status is NodeStatus.Offline or NodeStatus.Decommissioned or NodeStatus.Maintenance)
        {
            return ServiceResult.Fail<ReservationResponse>("node_unavailable");
        }

        // Check if sufficient capacity is available
        var availableCapacity = await GetAvailableCapacityInternalAsync(node, ct);
        if (availableCapacity is null)
        {
            return ServiceResult.Fail<ReservationResponse>("capacity_data_missing");
        }

        var requestedMemoryBytes = (long)memoryMb * 1024 * 1024;
        var requestedDiskBytes = (long)diskMb * 1024 * 1024;

        if (requestedMemoryBytes > availableCapacity.EffectiveAvailableMemoryBytes)
        {
            _logger.LogWarning(
                "Insufficient memory for reservation on node {NodeId}. Requested: {RequestedMb}MB, Available: {AvailableMb}MB",
                nodeId, memoryMb, availableCapacity.EffectiveAvailableMemoryBytes / (1024 * 1024));
            return ServiceResult.Fail<ReservationResponse>("insufficient_memory");
        }

        if (requestedDiskBytes > availableCapacity.EffectiveAvailableDiskBytes)
        {
            _logger.LogWarning(
                "Insufficient disk for reservation on node {NodeId}. Requested: {RequestedMb}MB, Available: {AvailableMb}MB",
                nodeId, diskMb, availableCapacity.EffectiveAvailableDiskBytes / (1024 * 1024));
            return ServiceResult.Fail<ReservationResponse>("insufficient_disk");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var reservation = new CapacityReservation
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            ReservationToken = Guid.NewGuid(),
            MemoryMb = memoryMb,
            DiskMb = diskMb,
            CpuMillicores = cpuMillicores,
            RequestedBy = requestedBy,
            CorrelationId = correlationId,
            Status = ReservationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(ttlMinutes)
        };

        _dbContext.CapacityReservations.Add(reservation);
        await _dbContext.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new CapacityReserved(
            nodeId,
            reservation.ReservationToken,
            memoryMb,
            diskMb,
            cpuMillicores,
            reservation.ExpiresAt,
            requestedBy), ct);

        NodesMetrics.ReservationsCreated.Add(1);

        _logger.LogInformation(
            "Created capacity reservation {ReservationToken} on node {NodeId} for {MemoryMb}MB memory, {DiskMb}MB disk",
            reservation.ReservationToken, nodeId, memoryMb, diskMb);

        return ServiceResult.Ok(MapToResponse(reservation));
    }

    public async Task<ServiceResult<ReservationResponse>> ClaimAsync(
        Guid reservationToken,
        string serverId,
        CancellationToken ct = default)
    {
        var reservation = await _dbContext.CapacityReservations
            .FirstOrDefaultAsync(r => r.ReservationToken == reservationToken, ct);

        if (reservation is null)
        {
            return ServiceResult.Fail<ReservationResponse>("reservation_not_found");
        }

        if (reservation.Status != ReservationStatus.Pending)
        {
            return ServiceResult.Fail<ReservationResponse>($"reservation_{reservation.Status.ToString().ToLowerInvariant()}");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (now > reservation.ExpiresAt)
        {
            reservation.Status = ReservationStatus.Expired;
            await _dbContext.SaveChangesAsync(ct);
            return ServiceResult.Fail<ReservationResponse>("reservation_expired");
        }

        reservation.Status = ReservationStatus.Claimed;
        reservation.ServerId = serverId;
        reservation.ClaimedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new CapacityClaimed(
            reservation.NodeId,
            reservationToken,
            serverId,
            now), ct);

        NodesMetrics.ReservationsClaimed.Add(1);

        _logger.LogInformation(
            "Claimed capacity reservation {ReservationToken} for server {ServerId}",
            reservationToken, serverId);

        return ServiceResult.Ok(MapToResponse(reservation));
    }

    public async Task<ServiceResult<bool>> ReleaseAsync(
        Guid reservationToken,
        string? reason = null,
        CancellationToken ct = default)
    {
        var reservation = await _dbContext.CapacityReservations
            .FirstOrDefaultAsync(r => r.ReservationToken == reservationToken, ct);

        if (reservation is null)
        {
            return ServiceResult.Fail<bool>("reservation_not_found");
        }

        if (reservation.Status is ReservationStatus.Released or ReservationStatus.Expired)
        {
            return ServiceResult.Fail<bool>("reservation_already_released");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        reservation.Status = ReservationStatus.Released;
        reservation.ReleasedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        // Publish event
        await _publishEndpoint.Publish(new CapacityReleased(
            reservation.NodeId,
            reservationToken,
            reason ?? "Explicit release"), ct);

        NodesMetrics.ReservationsReleased.Add(1);

        _logger.LogInformation(
            "Released capacity reservation {ReservationToken}. Reason: {Reason}",
            reservationToken, reason ?? "Explicit release");

        return ServiceResult.Ok(true);
    }

    public async Task<ServiceResult<ReservationResponse>> GetByTokenAsync(
        Guid reservationToken,
        CancellationToken ct = default)
    {
        var reservation = await _dbContext.CapacityReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationToken == reservationToken, ct);

        if (reservation is null)
        {
            return ServiceResult.Fail<ReservationResponse>("reservation_not_found");
        }

        return ServiceResult.Ok(MapToResponse(reservation));
    }

    public async Task<ServiceResult<AvailableCapacityResponse>> GetAvailableCapacityAsync(
        Guid nodeId,
        CancellationToken ct = default)
    {
        var node = await _dbContext.Nodes
            .AsNoTracking()
            .Include(n => n.Capacity)
            .Include(n => n.HardwareInventory)
            .FirstOrDefaultAsync(n => n.Id == nodeId, ct);

        if (node is null)
        {
            return ServiceResult.Fail<AvailableCapacityResponse>("node_not_found");
        }

        var capacity = await GetAvailableCapacityInternalAsync(node, ct);
        if (capacity is null)
        {
            return ServiceResult.Fail<AvailableCapacityResponse>("capacity_data_missing");
        }

        return ServiceResult.Ok(capacity);
    }

    public async Task<IReadOnlyList<ReservationSummary>> GetActiveReservationsAsync(
        Guid nodeId,
        CancellationToken ct = default)
    {
        var reservations = await _dbContext.CapacityReservations
            .AsNoTracking()
            .Where(r => r.NodeId == nodeId &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Claimed))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReservationSummary(
                r.Id,
                r.ReservationToken,
                r.MemoryMb,
                r.DiskMb,
                r.CpuMillicores,
                r.ServerId,
                r.RequestedBy,
                r.Status,
                r.ExpiresAt))
            .ToListAsync(ct);

        return reservations;
    }

    public async Task<int> ExpireStaleReservationsAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Find all pending reservations that have expired
        var expiredReservations = await _dbContext.CapacityReservations
            .Where(r => r.Status == ReservationStatus.Pending && r.ExpiresAt < now)
            .ToListAsync(ct);

        if (expiredReservations.Count == 0)
        {
            return 0;
        }

        foreach (var reservation in expiredReservations)
        {
            reservation.Status = ReservationStatus.Expired;
            reservation.ReleasedAt = now;

            // Publish event for each expired reservation
            await _publishEndpoint.Publish(new CapacityReservationExpired(
                reservation.NodeId,
                reservation.ReservationToken,
                now), ct);
        }

        await _dbContext.SaveChangesAsync(ct);

        NodesMetrics.ReservationsExpired.Add(expiredReservations.Count);

        _logger.LogInformation(
            "Expired {Count} stale capacity reservations",
            expiredReservations.Count);

        return expiredReservations.Count;
    }

    private async Task<AvailableCapacityResponse?> GetAvailableCapacityInternalAsync(
        Node node,
        CancellationToken ct)
    {
        if (node.Capacity is null || node.HardwareInventory is null)
        {
            return null;
        }

        // Calculate reserved resources from active reservations (Pending and Claimed)
        var activeReservations = await _dbContext.CapacityReservations
            .AsNoTracking()
            .Where(r => r.NodeId == node.Id &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Claimed))
            .ToListAsync(ct);

        var reservedMemoryBytes = activeReservations.Sum(r => (long)r.MemoryMb * 1024 * 1024);
        var reservedDiskBytes = activeReservations.Sum(r => (long)r.DiskMb * 1024 * 1024);
        var reservedSlots = activeReservations.Count;

        var effectiveAvailableMemory = Math.Max(0, node.Capacity.AvailableMemoryBytes - reservedMemoryBytes);
        var effectiveAvailableDisk = Math.Max(0, node.Capacity.AvailableDiskBytes - reservedDiskBytes);
        var effectiveAvailableSlots = Math.Max(0,
            node.Capacity.MaxGameServers - node.Capacity.CurrentGameServers - reservedSlots);

        return new AvailableCapacityResponse(
            node.Id,
            node.HardwareInventory.MemoryBytes,
            node.Capacity.AvailableMemoryBytes,
            reservedMemoryBytes,
            effectiveAvailableMemory,
            node.HardwareInventory.DiskBytes,
            node.Capacity.AvailableDiskBytes,
            reservedDiskBytes,
            effectiveAvailableDisk,
            node.Capacity.MaxGameServers,
            node.Capacity.CurrentGameServers,
            reservedSlots,
            effectiveAvailableSlots,
            activeReservations.Count);
    }

    private static ReservationResponse MapToResponse(CapacityReservation reservation)
    {
        return new ReservationResponse(
            reservation.Id,
            reservation.NodeId,
            reservation.ReservationToken,
            reservation.MemoryMb,
            reservation.DiskMb,
            reservation.CpuMillicores,
            reservation.ServerId,
            reservation.RequestedBy,
            reservation.Status,
            reservation.CreatedAt,
            reservation.ExpiresAt,
            reservation.ClaimedAt,
            reservation.ReleasedAt);
    }
}
