using System.ComponentModel.DataAnnotations;

namespace Dhadgar.ServiceDefaults.Resilience;

/// <summary>
/// Configuration options for the circuit breaker middleware.
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Configuration section name for binding.
    /// </summary>
    public const string SectionName = "CircuitBreaker";

    /// <summary>
    /// Number of consecutive failures before the circuit opens.
    /// </summary>
    [Range(1, 100)]
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Number of successful requests required to close the circuit from half-open state.
    /// </summary>
    [Range(1, 100)]
    public int SuccessThreshold { get; set; } = 2;

    /// <summary>
    /// Duration in seconds the circuit stays open before transitioning to half-open.
    /// </summary>
    [Range(1, 3600)]
    public int OpenDurationSeconds { get; set; } = 30;

    /// <summary>
    /// HTTP status codes that count as failures.
    /// </summary>
    // CA1819: Array property is required for IConfiguration binding compatibility.
    // Options pattern requires mutable arrays for section binding to work correctly.
#pragma warning disable CA1819
    public int[] FailureStatusCodes { get; set; } = [500, 502, 503, 504];
#pragma warning restore CA1819

    /// <summary>
    /// Whether to include the service/cluster name in error responses.
    /// Set to false in production to prevent information disclosure.
    /// </summary>
    public bool IncludeServiceNameInErrors { get; set; }
}
