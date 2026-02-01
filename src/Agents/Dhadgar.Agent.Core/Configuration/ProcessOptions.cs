using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Configuration for process management.
/// </summary>
public sealed class ProcessOptions
{
    /// <summary>
    /// Base directory for game server files.
    /// </summary>
    [Required]
    public string ServerBasePath { get; set; } = string.Empty;

    /// <summary>
    /// Maximum concurrent game server processes.
    /// </summary>
    [Range(1, 100)]
    public int MaxConcurrentServers { get; set; } = 10;

    /// <summary>
    /// Graceful shutdown timeout in seconds.
    /// </summary>
    [Range(5, 300)]
    public int GracefulShutdownTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Enable process resource isolation (Job Objects on Windows, cgroups on Linux).
    /// </summary>
    public bool EnableResourceIsolation { get; set; } = true;

    /// <summary>
    /// Default CPU limit as percentage (0 = no limit).
    /// </summary>
    [Range(0, 100)]
    public int DefaultCpuLimitPercent { get; set; }

    /// <summary>
    /// Default memory limit in megabytes (0 = no limit).
    /// </summary>
    [Range(0, int.MaxValue)]
    public int DefaultMemoryLimitMb { get; set; }

    /// <summary>
    /// Interval for collecting process metrics in seconds.
    /// </summary>
    [Range(5, 60)]
    public int MetricsCollectionIntervalSeconds { get; set; } = 15;
}
