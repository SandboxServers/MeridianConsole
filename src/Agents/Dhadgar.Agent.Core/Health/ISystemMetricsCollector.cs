using Dhadgar.Shared.Results;

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
    /// <returns>Result containing current system metrics on success, or error on failure.</returns>
    Task<Result<SystemMetrics>> CollectAsync(CancellationToken cancellationToken = default);
}
