using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Represents a capacity reservation on a node.
/// Reservations lock resources temporarily until claimed by a server deployment
/// or automatically released when they expire.
/// </summary>
public sealed class CapacityReservation
{
    public Guid Id { get; set; }

    /// <summary>The node this reservation is for.</summary>
    public Guid NodeId { get; set; }

    /// <summary>Navigation property to the node.</summary>
    public Node Node { get; set; } = null!;

    /// <summary>Unique token used to claim or release this reservation.</summary>
    public Guid ReservationToken { get; set; }

    /// <summary>Reserved memory in megabytes.</summary>
    public int MemoryMb { get; set; }

    /// <summary>Reserved disk space in megabytes.</summary>
    public int DiskMb { get; set; }

    /// <summary>Reserved CPU in millicores. 0 means not reserved.</summary>
    public int CpuMillicores { get; set; }

    /// <summary>Server ID that claimed this reservation. Filled when status is Claimed.</summary>
    [MaxLength(100)]
    public string? ServerId { get; set; }

    /// <summary>Service or user that requested this reservation.</summary>
    [Required]
    [MaxLength(200)]
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>Correlation ID for distributed tracing.</summary>
    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    /// <summary>Current status of the reservation.</summary>
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    /// <summary>When the reservation was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the reservation expires if not claimed.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When the reservation was claimed. Null if not yet claimed.</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>When the reservation was released. Null if not yet released.</summary>
    public DateTime? ReleasedAt { get; set; }
}

/// <summary>
/// Status of a capacity reservation.
/// </summary>
public enum ReservationStatus
{
    /// <summary>Reservation is active and awaiting claim.</summary>
    Pending = 0,

    /// <summary>Reservation has been claimed by a server deployment.</summary>
    Claimed = 1,

    /// <summary>Reservation has been explicitly released.</summary>
    Released = 2,

    /// <summary>Reservation expired without being claimed.</summary>
    Expired = 3
}
