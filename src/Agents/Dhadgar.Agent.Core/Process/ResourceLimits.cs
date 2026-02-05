namespace Dhadgar.Agent.Core.Process;

/// <summary>
/// Resource limits for a managed process.
/// </summary>
public sealed class ResourceLimits
{
    /// <summary>
    /// CPU limit as percentage of total capacity (1-100).
    /// Null means no limit.
    /// </summary>
    public int? CpuPercent { get; init; }

    /// <summary>
    /// Memory limit in megabytes.
    /// Null means no limit.
    /// </summary>
    public int? MemoryMb { get; init; }

    /// <summary>
    /// Whether to kill the process when memory limit is exceeded.
    /// If false, the process is throttled via swap (Linux) or working set trim (Windows).
    /// </summary>
    public bool KillOnMemoryExceeded { get; init; } = true;

    /// <summary>
    /// Maximum number of file handles/descriptors.
    /// </summary>
    public int? MaxFileHandles { get; init; }

    /// <summary>
    /// Maximum number of child processes allowed.
    /// </summary>
    public int? MaxChildProcesses { get; init; }

    /// <summary>
    /// Creates limits from configuration percentages.
    /// </summary>
    /// <param name="cpuPercent">CPU percentage (clamped to 1-100 range if positive).</param>
    /// <param name="memoryMb">Memory limit in megabytes.</param>
    public static ResourceLimits FromConfig(int cpuPercent, int memoryMb)
    {
        // Clamp CPU percent to valid 1-100 range if positive
        int? clampedCpuPercent = cpuPercent > 0
            ? Math.Clamp(cpuPercent, 1, 100)
            : null;

        return new ResourceLimits
        {
            CpuPercent = clampedCpuPercent,
            MemoryMb = memoryMb > 0 ? memoryMb : null
        };
    }
}
