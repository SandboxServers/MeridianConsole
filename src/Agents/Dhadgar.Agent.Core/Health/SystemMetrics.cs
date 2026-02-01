namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// System resource metrics collected for heartbeat.
/// </summary>
public sealed class SystemMetrics
{
    /// <summary>
    /// CPU usage percentage (0-100).
    /// </summary>
    public double CpuUsagePercent { get; init; }

    /// <summary>
    /// Total physical memory in bytes.
    /// </summary>
    public long TotalMemoryBytes { get; init; }

    /// <summary>
    /// Available physical memory in bytes.
    /// </summary>
    public long AvailableMemoryBytes { get; init; }

    /// <summary>
    /// Used memory in bytes.
    /// </summary>
    public long UsedMemoryBytes => TotalMemoryBytes - AvailableMemoryBytes;

    /// <summary>
    /// Memory usage percentage (0-100).
    /// </summary>
    public double MemoryUsagePercent =>
        TotalMemoryBytes > 0 ? (double)UsedMemoryBytes / TotalMemoryBytes * 100 : 0;

    /// <summary>
    /// Disk space information for relevant volumes.
    /// </summary>
    public List<DiskMetrics> Disks { get; init; } = [];

    /// <summary>
    /// Network interface metrics.
    /// </summary>
    public List<NetworkMetrics> Networks { get; init; } = [];

    /// <summary>
    /// System uptime.
    /// </summary>
    public TimeSpan SystemUptime { get; init; }

    /// <summary>
    /// Number of logical processors available (respects cgroups/Job Objects).
    /// </summary>
    public int ProcessorCount { get; init; }

    /// <summary>
    /// Operating system description.
    /// </summary>
    public string? OsDescription { get; init; }
}

/// <summary>
/// Disk/volume metrics.
/// </summary>
public sealed class DiskMetrics
{
    /// <summary>
    /// Volume/mount point name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Total space in bytes.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Available space in bytes.
    /// </summary>
    public long AvailableBytes { get; init; }

    /// <summary>
    /// Used space in bytes.
    /// </summary>
    public long UsedBytes => TotalBytes - AvailableBytes;

    /// <summary>
    /// Usage percentage (0-100).
    /// </summary>
    public double UsagePercent =>
        TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
}

/// <summary>
/// Network interface metrics.
/// </summary>
public sealed class NetworkMetrics
{
    /// <summary>
    /// Interface name.
    /// </summary>
    public required string InterfaceName { get; init; }

    /// <summary>
    /// Bytes received since last collection.
    /// </summary>
    public long BytesReceived { get; init; }

    /// <summary>
    /// Bytes sent since last collection.
    /// </summary>
    public long BytesSent { get; init; }

    /// <summary>
    /// Interface is operational.
    /// </summary>
    public bool IsOperational { get; init; }
}
