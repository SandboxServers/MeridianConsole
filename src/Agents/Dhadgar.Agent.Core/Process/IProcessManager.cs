using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Process;

/// <summary>
/// Platform-agnostic interface for process management.
/// Windows implements using Job Objects, Linux uses cgroups v2.
/// </summary>
public interface IProcessManager
{
    /// <summary>
    /// Start a new process with the specified configuration.
    /// </summary>
    /// <param name="config">Process configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Started process info.</returns>
    Task<Result<ManagedProcess>> StartProcessAsync(
        ProcessConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop a running process gracefully.
    /// </summary>
    /// <param name="processId">Process identifier.</param>
    /// <param name="timeout">Timeout for graceful shutdown.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<bool>> StopProcessAsync(
        Guid processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kill a process immediately.
    /// </summary>
    /// <param name="processId">Process identifier.</param>
    Task<Result<bool>> KillProcessAsync(Guid processId);

    /// <summary>
    /// Get status of a managed process.
    /// </summary>
    /// <param name="processId">Process identifier.</param>
    /// <returns>Process status, or null if not found.</returns>
    ManagedProcess? GetProcess(Guid processId);

    /// <summary>
    /// Get all managed processes.
    /// </summary>
    IReadOnlyList<ManagedProcess> GetAllProcesses();

    /// <summary>
    /// Update resource limits for a running process.
    /// </summary>
    /// <param name="processId">Process identifier.</param>
    /// <param name="limits">New resource limits.</param>
    Task<Result<bool>> UpdateResourceLimitsAsync(
        Guid processId,
        ResourceLimits limits);

    /// <summary>
    /// Event raised when a process exits.
    /// </summary>
    event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    /// <summary>
    /// Event raised when process output is received.
    /// </summary>
    event EventHandler<ProcessOutputEventArgs>? OutputReceived;
}

/// <summary>
/// Event args for process exit.
/// </summary>
public sealed class ProcessExitedEventArgs : EventArgs
{
    public required Guid ProcessId { get; init; }
    public required int ExitCode { get; init; }
    public required DateTimeOffset ExitTime { get; init; }
    public bool WasKilled { get; init; }
}

/// <summary>
/// Event args for process output.
/// </summary>
public sealed class ProcessOutputEventArgs : EventArgs
{
    public required Guid ProcessId { get; init; }
    public required string Data { get; init; }
    public required bool IsError { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
