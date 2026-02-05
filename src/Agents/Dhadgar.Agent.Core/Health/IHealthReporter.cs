using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Interface for reporting health status to the control plane.
/// </summary>
public interface IHealthReporter
{
    /// <summary>
    /// Current node status.
    /// </summary>
    NodeStatus Status { get; }

    /// <summary>
    /// Set the node status.
    /// </summary>
    /// <param name="status">New status.</param>
    /// <param name="reason">Reason for status change.</param>
    void SetStatus(NodeStatus status, string? reason = null);

    /// <summary>
    /// Add a warning that will be included in the next heartbeat.
    /// </summary>
    /// <param name="warning">Warning message.</param>
    /// <remarks>
    /// Implementations should enforce a maximum warning count (e.g., 100) to prevent
    /// unbounded memory growth. When the limit is reached, oldest warnings should be evicted.
    /// </remarks>
    void AddWarning(string warning);

    /// <summary>
    /// Clear all warnings.
    /// </summary>
    void ClearWarnings();

    /// <summary>
    /// Get the current heartbeat payload.
    /// </summary>
    /// <returns>A Result containing the heartbeat payload, or failure if the agent is not enrolled.</returns>
    Task<Result<HeartbeatPayload>> GetHeartbeatPayloadAsync(CancellationToken cancellationToken = default);
}
