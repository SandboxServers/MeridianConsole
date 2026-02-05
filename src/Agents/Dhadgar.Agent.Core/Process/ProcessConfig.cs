using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Agent.Core.Process;

/// <summary>
/// Configuration for starting a new process.
/// </summary>
public sealed class ProcessConfig : IValidatableObject
{
    /// <summary>
    /// Minimum allowed restart delay (1 second).
    /// </summary>
    public static readonly TimeSpan MinRestartDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum allowed restart delay (10 minutes).
    /// </summary>
    public static readonly TimeSpan MaxRestartDelay = TimeSpan.FromMinutes(10);
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
    [Range(0, int.MaxValue)]
    public int MaxRestartAttempts { get; init; } = 3;

    /// <summary>
    /// Delay between restart attempts.
    /// Must be between 1 second and 10 minutes.
    /// </summary>
    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate RestartDelay bounds to prevent rapid restart loops or excessive delays
        if (RestartDelay < MinRestartDelay)
        {
            yield return new ValidationResult(
                $"{nameof(RestartDelay)} must be at least {MinRestartDelay.TotalSeconds} second(s)",
                [nameof(RestartDelay)]);
        }
        else if (RestartDelay > MaxRestartDelay)
        {
            yield return new ValidationResult(
                $"{nameof(RestartDelay)} must not exceed {MaxRestartDelay.TotalMinutes} minute(s)",
                [nameof(RestartDelay)]);
        }
    }
}
