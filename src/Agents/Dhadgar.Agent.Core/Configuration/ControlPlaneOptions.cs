using System.ComponentModel.DataAnnotations;
using Dhadgar.Contracts.Nodes;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Configuration for control plane connectivity.
/// </summary>
public sealed class ControlPlaneOptions : IValidatableObject
{
    /// <summary>
    /// Control plane endpoint URL.
    /// </summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = "https://api.meridianconsole.com";

    /// <summary>
    /// Heartbeat interval in seconds.
    /// </summary>
    [Range(10, 300)]
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Initial reconnect delay in seconds.
    /// </summary>
    [Range(1, 60)]
    public int ReconnectDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Maximum reconnect delay in seconds (exponential backoff cap).
    /// </summary>
    [Range(60, 3600)]
    public int MaxReconnectDelaySeconds { get; set; } = 300;

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    [Range(5, 120)]
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Keep-alive interval in seconds (for detecting stale connections).
    /// </summary>
    [Range(5, 60)]
    public int KeepAliveIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Server timeout in seconds (disconnect if no message received).
    /// </summary>
    [Range(30, 300)]
    public int ServerTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Invocation timeout in seconds for individual SignalR hub method calls.
    /// Prevents hanging on slow or unresponsive server methods.
    /// </summary>
    [Range(5, 120)]
    public int InvocationTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Minutes between certificate renewal checks while the agent runs (default 720, i.e. 12 hours).
    /// </summary>
    public int RenewalCheckIntervalMinutes { get; set; } = 720;

    /// <summary>
    /// Minutes before a failed renewal is retried. It stays well inside Nodes' revocation grace window (D-S212).
    /// </summary>
    public int RenewalRetryMinutes { get; set; } = AgentCertificateRenewalPolicy.AgentRetryMinutes;

    /// <summary>
    /// Seconds between connection and renewal checks in the bootstrap loop.
    /// </summary>
    public int ConnectionCheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Seconds before the first enrollment retry. The delay doubles after each failed attempt, up to 15 minutes.
    /// </summary>
    public int EnrollmentRetrySeconds { get; set; } = 60;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // SECURITY: Enforce HTTPS at configuration time to fail fast on insecure endpoints
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpointUri) ||
            !string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                $"{nameof(Endpoint)} must be an absolute HTTPS URL",
                [nameof(Endpoint)]);
        }

        if (MaxReconnectDelaySeconds < ReconnectDelaySeconds)
        {
            yield return new ValidationResult(
                $"{nameof(MaxReconnectDelaySeconds)} ({MaxReconnectDelaySeconds}) must be greater than or equal to {nameof(ReconnectDelaySeconds)} ({ReconnectDelaySeconds})",
                [nameof(MaxReconnectDelaySeconds), nameof(ReconnectDelaySeconds)]);
        }

        if (ServerTimeoutSeconds <= KeepAliveIntervalSeconds)
        {
            yield return new ValidationResult(
                $"{nameof(ServerTimeoutSeconds)} ({ServerTimeoutSeconds}) must be greater than {nameof(KeepAliveIntervalSeconds)} ({KeepAliveIntervalSeconds})",
                [nameof(ServerTimeoutSeconds), nameof(KeepAliveIntervalSeconds)]);
        }

        if (RenewalCheckIntervalMinutes is < 1 or > 10080)
        {
            yield return new ValidationResult(
                $"{nameof(RenewalCheckIntervalMinutes)} ({RenewalCheckIntervalMinutes}) must be between 1 and 10080",
                [nameof(RenewalCheckIntervalMinutes)]);
        }

        if (RenewalRetryMinutes is < 1 or > 30)
        {
            yield return new ValidationResult(
                $"{nameof(RenewalRetryMinutes)} ({RenewalRetryMinutes}) must be between 1 and 30 so retries stay inside the revocation grace window",
                [nameof(RenewalRetryMinutes)]);
        }

        if (ConnectionCheckIntervalSeconds is < 1 or > 3600)
        {
            yield return new ValidationResult(
                $"{nameof(ConnectionCheckIntervalSeconds)} ({ConnectionCheckIntervalSeconds}) must be between 1 and 3600",
                [nameof(ConnectionCheckIntervalSeconds)]);
        }

        if (EnrollmentRetrySeconds is < 1 or > 3600)
        {
            yield return new ValidationResult(
                $"{nameof(EnrollmentRetrySeconds)} ({EnrollmentRetrySeconds}) must be between 1 and 3600",
                [nameof(EnrollmentRetrySeconds)]);
        }
    }
}
