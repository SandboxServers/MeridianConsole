namespace Dhadgar.Agent.Core.Process;

/// <summary>
/// Configuration for starting a new process.
/// </summary>
public sealed class ProcessConfig
{
    /// <summary>
    /// Server identifier from control plane.
    /// </summary>
    public required Guid ServerId { get; init; }

    /// <summary>
    /// Path to the executable.
    /// </summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Command line arguments.
    /// </summary>
    public string? Arguments { get; init; }

    /// <summary>
    /// Working directory for the process.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Environment variables to set.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; init; } = [];

    /// <summary>
    /// Resource limits for the process.
    /// </summary>
    public ResourceLimits? Limits { get; init; }

    /// <summary>
    /// Capture standard output.
    /// </summary>
    public bool CaptureStdout { get; init; } = true;

    /// <summary>
    /// Capture standard error.
    /// </summary>
    public bool CaptureStderr { get; init; } = true;

    /// <summary>
    /// Automatically restart if the process exits unexpectedly.
    /// </summary>
    public bool AutoRestart { get; init; }

    /// <summary>
    /// Maximum restart attempts before giving up.
    /// </summary>
    public int MaxRestartAttempts { get; init; } = 3;

    /// <summary>
    /// Delay between restart attempts.
    /// </summary>
    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(5);
}
