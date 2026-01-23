using Dhadgar.Nodes.Models;

namespace Dhadgar.Nodes.Services;

/// <summary>
/// Service for managing capacity reservations on nodes.
/// </summary>
public interface ICapacityReservationService
{
    /// <summary>
    /// Creates a new capacity reservation on a node.
    /// </summary>
    /// <param name="nodeId">The node to reserve capacity on.</param>
    /// <param name="memoryMb">Memory to reserve in megabytes.</param>
    /// <param name="diskMb">Disk space to reserve in megabytes.</param>
    /// <param name="cpuMillicores">CPU to reserve in millicores (0 = not reserved).</param>
    /// <param name="requestedBy">Service or user requesting the reservation.</param>
    /// <param name="ttlMinutes">Time-to-live before automatic expiration.</param>
    /// <param name="correlationId">Optional correlation ID for tracing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created reservation or error.</returns>
    Task<ServiceResult<ReservationResponse>> ReserveAsync(
        Guid nodeId,
        int memoryMb,
        int diskMb,
        int cpuMillicores,
        string requestedBy,
        int ttlMinutes,
        string? correlationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Claims a reservation with a server ID.
    /// </summary>
    /// <param name="reservationToken">The unique reservation token.</param>
    /// <param name="serverId">The server ID claiming this reservation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success/failure result.</returns>
    Task<ServiceResult<ReservationResponse>> ClaimAsync(
        Guid reservationToken,
        string serverId,
        CancellationToken ct = default);

    /// <summary>
    /// Releases a reservation, freeing the capacity.
    /// </summary>
    /// <param name="reservationToken">The unique reservation token.</param>
    /// <param name="reason">Optional reason for release.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success/failure result.</returns>
    Task<ServiceResult<bool>> ReleaseAsync(
        Guid reservationToken,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a reservation by its token.
    /// </summary>
    Task<ServiceResult<ReservationResponse>> GetByTokenAsync(
        Guid reservationToken,
        CancellationToken ct = default);

    /// <summary>
    /// Gets available capacity on a node, accounting for active reservations.
    /// </summary>
    Task<ServiceResult<AvailableCapacityResponse>> GetAvailableCapacityAsync(
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists active reservations for a node.
    /// </summary>
    Task<IReadOnlyList<ReservationSummary>> GetActiveReservationsAsync(
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Expires stale reservations that have passed their expiration time.
    /// </summary>
    /// <returns>Number of reservations expired.</returns>
    Task<int> ExpireStaleReservationsAsync(CancellationToken ct = default);
}
