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
    void AddWarning(string warning);

    /// <summary>
    /// Clear all warnings.
    /// </summary>
    void ClearWarnings();

    /// <summary>
    /// Get the current heartbeat payload.
    /// </summary>
    /// <returns>Heartbeat payload with current status and metrics.</returns>
    Task<HeartbeatPayload> GetHeartbeatPayloadAsync(CancellationToken cancellationToken = default);
}
