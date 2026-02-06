using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Agent.Core.Configuration;

/// <summary>
/// Configuration for process management.
/// </summary>
public sealed class ProcessOptions : IValidatableObject
{
    /// <summary>
    /// Base directory for game server files.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
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
    /// Maximum memory limit in MB (1 TB).
    /// </summary>
    public const int MaxMemoryLimitMb = 1_048_576;

    /// <summary>
    /// Default memory limit in megabytes (0 = no limit).
    /// </summary>
    [Range(0, MaxMemoryLimitMb)]
    public int DefaultMemoryLimitMb { get; set; }

    /// <summary>
    /// Interval for collecting process metrics in seconds.
    /// </summary>
    [Range(5, 60)]
    public int MetricsCollectionIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Enable Windows Service-based isolation for game servers (Windows only).
    /// When enabled, each game server runs as a separate Windows Service with
    /// its own Virtual Service Account and file ACL isolation.
    /// Default is false (uses Job Object-based isolation instead).
    /// </summary>
    /// <remarks>
    /// Service isolation provides stronger security boundaries:
    /// - Each server runs under its own NT SERVICE\MeridianGS_{id} account
    /// - File ACLs restrict each server to its own directory
    /// - Processes are managed via Windows Service Control Manager
    /// - Requires the GameServerWrapper executable to be deployed
    /// </remarks>
    public bool UseServiceIsolation { get; set; }

    /// <summary>
    /// Path to the GameServerWrapper executable (required when UseServiceIsolation is true).
    /// Must be an absolute path. Only applicable on Windows.
    /// </summary>
    public string? GameServerWrapperPath { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Service isolation is only supported on Windows - fail fast with clear message
        if (UseServiceIsolation && !OperatingSystem.IsWindows())
        {
            yield return new ValidationResult(
                $"{nameof(UseServiceIsolation)} is only supported on Windows",
                [nameof(UseServiceIsolation)]);
        }

        // Validate GameServerWrapperPath when service isolation is enabled on Windows
        if (UseServiceIsolation && OperatingSystem.IsWindows())
        {
            if (string.IsNullOrWhiteSpace(GameServerWrapperPath))
            {
                yield return new ValidationResult(
                    $"{nameof(GameServerWrapperPath)} is required when {nameof(UseServiceIsolation)} is enabled",
                    [nameof(GameServerWrapperPath)]);
            }
            else
            {
                if (!Path.IsPathFullyQualified(GameServerWrapperPath))
                {
                    yield return new ValidationResult(
                        $"{nameof(GameServerWrapperPath)} must be a fully qualified absolute path",
                        [nameof(GameServerWrapperPath)]);
                }
                else
                {
                    // Validate path is normalized (no traversal)
                    var (normalizedPath, isValid) = TryGetFullPath(GameServerWrapperPath);
                    if (!isValid)
                    {
                        yield return new ValidationResult(
                            $"{nameof(GameServerWrapperPath)} is not a valid path",
                            [nameof(GameServerWrapperPath)]);
                    }
                    else
                    {
                        // Reject path traversal sequences - Windows is case-insensitive
                        var comparison = StringComparison.OrdinalIgnoreCase;
                        var normalizedTrimmed = normalizedPath!.TrimEnd(Path.DirectorySeparatorChar);
                        var originalTrimmed = GameServerWrapperPath.TrimEnd(Path.DirectorySeparatorChar);

                        if (!originalTrimmed.Equals(normalizedTrimmed, comparison))
                        {
                            yield return new ValidationResult(
                                $"{nameof(GameServerWrapperPath)} contains path traversal sequences",
                                [nameof(GameServerWrapperPath)]);
                        }
                    }
                }
            }
        }

        // ServerBasePath must be a fully qualified, normalized path to prevent path traversal
        if (!string.IsNullOrEmpty(ServerBasePath))
        {
            if (!Path.IsPathFullyQualified(ServerBasePath))
            {
                yield return new ValidationResult(
                    $"{nameof(ServerBasePath)} must be a fully qualified absolute path",
                    [nameof(ServerBasePath)]);
            }
            else
            {
                // Ensure path is normalized (no .. or . components)
                var (normalizedPath, isValid) = TryGetFullPath(ServerBasePath);
                if (!isValid)
                {
                    yield return new ValidationResult(
                        $"{nameof(ServerBasePath)} is not a valid path",
                        [nameof(ServerBasePath)]);
                }
                else
                {
                    // Use OS-aware comparison: case-insensitive on Windows, case-sensitive on Unix
                    var comparison = OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;
                    var normalizedTrimmed = normalizedPath!.TrimEnd(Path.DirectorySeparatorChar);
                    var originalTrimmed = ServerBasePath.TrimEnd(Path.DirectorySeparatorChar);

                    if (!originalTrimmed.Equals(normalizedTrimmed, comparison))
                    {
                        yield return new ValidationResult(
                            $"{nameof(ServerBasePath)} must be a normalized absolute path (use '{normalizedPath}' instead)",
                            [nameof(ServerBasePath)]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Attempts to get the full path, returning success/failure without throwing.
    /// </summary>
    private static (string? NormalizedPath, bool IsValid) TryGetFullPath(string path)
    {
        try
        {
            return (Path.GetFullPath(path), true);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return (null, false);
        }
    }
}
