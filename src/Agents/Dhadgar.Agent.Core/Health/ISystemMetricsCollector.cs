namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Collects system metrics for health reporting.
/// </summary>
public interface ISystemMetricsCollector
{
    /// <summary>
    /// Collect current system metrics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current system metrics.</returns>
    Task<SystemMetrics> CollectAsync(CancellationToken cancellationToken = default);
}
