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

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // ServerBasePath must be an absolute, normalized path to prevent path traversal
        if (!string.IsNullOrEmpty(ServerBasePath))
        {
            if (!Path.IsPathRooted(ServerBasePath))
            {
                yield return new ValidationResult(
                    $"{nameof(ServerBasePath)} must be an absolute path",
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

                    if (!ServerBasePath.Equals(normalizedPath, comparison) &&
                        !ServerBasePath.Equals(normalizedPath!.TrimEnd(Path.DirectorySeparatorChar), comparison))
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
