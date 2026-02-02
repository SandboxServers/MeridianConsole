using Dhadgar.Agent.Core.Health;

namespace Dhadgar.Agent.Core.Process;

/// <summary>
/// Represents a managed process.
/// </summary>
public sealed class ManagedProcess
{
    /// <summary>
    /// Unique identifier for this managed process.
    /// </summary>
    public required Guid ProcessId { get; init; }

    /// <summary>
    /// Server identifier from control plane.
    /// </summary>
    public required Guid ServerId { get; init; }

    /// <summary>
    /// OS process ID. Null when process has not been started.
    /// </summary>
    public int? OsPid { get; set; }

    /// <summary>
    /// Current process state.
    /// </summary>
    public ProcessState State { get; set; }

    /// <summary>
    /// When the process was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// When the process exited (if applicable).
    /// </summary>
    public DateTimeOffset? ExitedAt { get; set; }

    /// <summary>
    /// Exit code (if process has exited).
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Current CPU usage percentage.
    /// </summary>
    public double CpuPercent { get; set; }

    /// <summary>
    /// Current memory usage in bytes.
    /// </summary>
    public long MemoryBytes { get; set; }

    /// <summary>
    /// Process configuration.
    /// </summary>
    public required ProcessConfig Config { get; init; }

    /// <summary>
    /// Number of times the process has been restarted.
    /// </summary>
    public int RestartCount { get; set; }

    /// <summary>
    /// Process uptime. Returns TimeSpan.Zero if process has not been started.
    /// </summary>
    public TimeSpan Uptime
    {
        get
        {
            if (StartedAt == default)
            {
                return TimeSpan.Zero;
            }

            return State == ProcessState.Running
                ? DateTimeOffset.UtcNow - StartedAt
                : (ExitedAt ?? DateTimeOffset.UtcNow) - StartedAt;
        }
    }

    /// <summary>
    /// Convert to status for heartbeat.
    /// </summary>
    public ProcessStatus ToStatus()
    {
        return new ProcessStatus
        {
            ProcessId = ProcessId,
            ServerId = ServerId,
            State = State,
            OsPid = OsPid,
            CpuPercent = CpuPercent,
            MemoryBytes = MemoryBytes,
            Uptime = Uptime
        };
    }
}
