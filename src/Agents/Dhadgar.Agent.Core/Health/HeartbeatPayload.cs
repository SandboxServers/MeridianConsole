namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Heartbeat payload sent to the control plane.
/// </summary>
public sealed class HeartbeatPayload
{
    /// <summary>
    /// Node identifier.
    /// </summary>
    public required Guid NodeId { get; init; }

    /// <summary>
    /// Current agent version.
    /// </summary>
    public required string AgentVersion { get; init; }

    /// <summary>
    /// Timestamp of this heartbeat.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Overall node status.
    /// </summary>
    public NodeStatus Status { get; init; } = NodeStatus.Online;

    /// <summary>
    /// System resource metrics.
    /// </summary>
    public SystemMetrics? Metrics { get; init; }

    /// <summary>
    /// Active game server processes.
    /// </summary>
    public IList<ProcessStatus> ActiveProcesses { get; init; } = [];

    /// <summary>
    /// Any warnings or issues to report.
    /// </summary>
    public IList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Node status levels.
/// </summary>
public enum NodeStatus
{
    /// <summary>
    /// Node is online and fully operational.
    /// </summary>
    Online,

    /// <summary>
    /// Node is operational but experiencing issues.
    /// </summary>
    Degraded,

    /// <summary>
    /// Node is starting up.
    /// </summary>
    Starting,

    /// <summary>
    /// Node is shutting down.
    /// </summary>
    ShuttingDown,

    /// <summary>
    /// Node is in maintenance mode.
    /// </summary>
    Maintenance
}

/// <summary>
/// Status of an active process.
/// </summary>
public sealed class ProcessStatus
{
    /// <summary>
    /// Process identifier assigned by the control plane.
    /// </summary>
    public required Guid ProcessId { get; init; }

    /// <summary>
    /// Server instance identifier.
    /// </summary>
    public required Guid ServerId { get; init; }

    /// <summary>
    /// Current process state.
    /// </summary>
    public ProcessState State { get; init; }

    /// <summary>
    /// OS process ID.
    /// </summary>
    public int? OsPid { get; init; }

    /// <summary>
    /// CPU usage percentage.
    /// </summary>
    public double CpuPercent { get; init; }

    /// <summary>
    /// Memory usage in bytes.
    /// </summary>
    public long MemoryBytes { get; init; }

    /// <summary>
    /// Process uptime.
    /// </summary>
    public TimeSpan Uptime { get; init; }
}

/// <summary>
/// Process state.
/// </summary>
public enum ProcessState
{
    /// <summary>
    /// Process is starting.
    /// </summary>
    Starting,

    /// <summary>
    /// Process is running normally.
    /// </summary>
    Running,

    /// <summary>
    /// Process is stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// Process has stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// Process crashed or failed.
    /// </summary>
    Failed
}
