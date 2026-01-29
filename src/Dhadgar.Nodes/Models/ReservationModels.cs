using System.ComponentModel.DataAnnotations;
using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Models;

/// <summary>
/// Request to create a capacity reservation on a node.
/// </summary>
public sealed record CreateReservationRequest(
    [Range(1, int.MaxValue, ErrorMessage = "MemoryMb must be at least 1")]
    int MemoryMb,

    [Range(1, int.MaxValue, ErrorMessage = "DiskMb must be at least 1")]
    int DiskMb,

    [Range(0, int.MaxValue, ErrorMessage = "CpuMillicores must be non-negative")]
    int CpuMillicores = 0,

    [Range(1, 1440, ErrorMessage = "TtlMinutes must be between 1 and 1440 (24 hours)")]
    int TtlMinutes = 15,

    [Required]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "RequestedBy must be between 1 and 200 characters")]
    string RequestedBy = "");

/// <summary>
/// Request to claim a reservation with a server ID.
/// </summary>
public sealed record ClaimReservationRequest(
    [Required]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "ServerId must be between 1 and 100 characters")]
    string ServerId);

/// <summary>
/// Response containing a newly created reservation.
/// </summary>
public sealed record ReservationResponse(
    Guid Id,
    Guid NodeId,
    Guid ReservationToken,
    int MemoryMb,
    int DiskMb,
    int CpuMillicores,
    string? ServerId,
    string RequestedBy,
    ReservationStatus Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? ClaimedAt,
    DateTime? ReleasedAt);

/// <summary>
/// Summary view of a reservation for list responses.
/// </summary>
public sealed record ReservationSummary(
    Guid Id,
    Guid ReservationToken,
    int MemoryMb,
    int DiskMb,
    int CpuMillicores,
    string? ServerId,
    string RequestedBy,
    ReservationStatus Status,
    DateTime ExpiresAt);

/// <summary>
/// Available capacity on a node after accounting for active reservations.
/// </summary>
public sealed record AvailableCapacityResponse(
    Guid NodeId,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    long ReservedMemoryBytes,
    long EffectiveAvailableMemoryBytes,
    long TotalDiskBytes,
    long AvailableDiskBytes,
    long ReservedDiskBytes,
    long EffectiveAvailableDiskBytes,
    int TotalCpuMillicores,
    int AvailableCpuMillicores,
    int ReservedCpuMillicores,
    int EffectiveAvailableCpuMillicores,
    int MaxGameServers,
    int CurrentGameServers,
    int ReservedSlots,
    int EffectiveAvailableSlots,
    int ActiveReservationCount);
