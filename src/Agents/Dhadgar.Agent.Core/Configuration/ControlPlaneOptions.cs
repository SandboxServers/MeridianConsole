using System.ComponentModel.DataAnnotations;

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

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
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
    }
}
