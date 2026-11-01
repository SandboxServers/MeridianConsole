using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Servers;

/// <summary>
/// Configuration options for the Servers service.
/// </summary>
public sealed class ServersOptions
{
    public const string SectionName = "Servers";

    /// <summary>
    /// Timeout in seconds for provisioning operations before marking as failed.
    /// </summary>
    [Range(60, 3600)]
    public int ProvisioningTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Default timeout in seconds for graceful shutdown.
    /// </summary>
    [Range(10, 300)]
    public int DefaultShutdownTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of auto-restart attempts for crashed servers.
    /// </summary>
    [Range(0, 10)]
    public int MaxAutoRestartAttempts { get; set; } = 3;

    /// <summary>
    /// Cooldown period in seconds between auto-restart attempts.
    /// </summary>
    [Range(30, 3600)]
    public int AutoRestartCooldownSeconds { get; set; } = 300;

    /// <summary>
    /// Delay in seconds before auto-restarting a crashed server.
    /// </summary>
    [Range(5, 300)]
    public int AutoRestartDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Interval in seconds for checking provisioning timeouts.
    /// </summary>
    [Range(10, 300)]
    public int ProvisioningCheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Interval in seconds for checking crashed servers that need auto-restart.
    /// </summary>
    [Range(10, 300)]
    public int AutoRestartCheckIntervalSeconds { get; set; } = 30;
}
