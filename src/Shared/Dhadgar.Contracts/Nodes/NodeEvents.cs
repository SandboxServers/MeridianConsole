namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// Published when a new node completes enrollment.
/// </summary>
public record NodeEnrolled(
    Guid NodeId,
    Guid OrganizationId,
    string Platform,
    DateTime EnrolledAt);

/// <summary>
/// Published when a node comes online (first heartbeat after being offline/enrolling).
/// </summary>
public record NodeOnline(
    Guid NodeId,
    DateTime Timestamp);

/// <summary>
/// Published when a node goes offline (no heartbeat within threshold).
/// </summary>
public record NodeOffline(
    Guid NodeId,
    DateTime Timestamp,
    string? Reason);

/// <summary>
/// Published when a node enters a degraded state (high resource usage or health issues).
/// </summary>
public record NodeDegraded(
    Guid NodeId,
    DateTime Timestamp,
    IReadOnlyList<string> Issues);

/// <summary>
/// Published when a node recovers from a degraded state.
/// </summary>
public record NodeRecovered(
    Guid NodeId,
    DateTime Timestamp);

/// <summary>
/// Published when a node is permanently decommissioned.
/// </summary>
public record NodeDecommissioned(
    Guid NodeId,
    DateTime Timestamp);

/// <summary>
/// Published when a node enters maintenance mode.
/// </summary>
public record NodeMaintenanceStarted(
    Guid NodeId,
    DateTime Timestamp);

/// <summary>
/// Published when a node exits maintenance mode.
/// </summary>
public record NodeMaintenanceEnded(
    Guid NodeId,
    DateTime Timestamp);

/// <summary>
/// Published when an agent certificate is issued.
/// </summary>
public record AgentCertificateIssued(
    Guid NodeId,
    string Thumbprint,
    DateTime ExpiresAt);

/// <summary>
/// Published when an agent certificate is revoked.
/// </summary>
public record AgentCertificateRevoked(
    Guid NodeId,
    string Thumbprint,
    string Reason);

/// <summary>
/// Published when an agent certificate is renewed.
/// </summary>
public record AgentCertificateRenewed(
    Guid NodeId,
    string OldThumbprint,
    string NewThumbprint);

// Capacity Reservation Events

/// <summary>
/// Published when capacity is reserved on a node.
/// </summary>
public record CapacityReserved(
    Guid NodeId,
    Guid ReservationToken,
    int MemoryMb,
    int DiskMb,
    int CpuMillicores,
    DateTime ExpiresAt,
    string RequestedBy);

/// <summary>
/// Published when a capacity reservation is claimed by a server.
/// </summary>
public record CapacityClaimed(
    Guid NodeId,
    Guid ReservationToken,
    string ServerId,
    DateTime ClaimedAt);

/// <summary>
/// Published when a capacity reservation is explicitly released.
/// </summary>
public record CapacityReleased(
    Guid NodeId,
    Guid ReservationToken,
    string Reason);

/// <summary>
/// Published when a capacity reservation expires without being claimed.
/// </summary>
public record CapacityReservationExpired(
    Guid NodeId,
    Guid ReservationToken,
    DateTime ExpiredAt);

/// <summary>
/// Published when a node's remaining capacity falls below the configured threshold.
/// </summary>
public record NodeCapacityLow(
    Guid NodeId,
    DateTime Timestamp,
    long AvailableMemoryBytes,
    long TotalMemoryBytes,
    long AvailableDiskBytes,
    long TotalDiskBytes,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    double ThresholdPercent);
