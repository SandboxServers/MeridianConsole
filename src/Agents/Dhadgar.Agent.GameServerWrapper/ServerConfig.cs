using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.GameServerWrapper;

/// <summary>
/// Configuration for launching a game server process.
/// </summary>
/// <remarks>
/// This configuration is written by the agent and read by the wrapper.
/// SECURITY: All paths are validated before use.
/// </remarks>
public sealed class ServerConfig
{
    /// <summary>
    /// Maximum configuration file size (1 MB).
    /// </summary>
    public const int MaxConfigFileSizeBytes = 1 * 1024 * 1024;

    /// <summary>
    /// Path to the game server executable.
    /// </summary>
    [JsonPropertyName("executablePath")]
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Command-line arguments for the game server.
    /// </summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }

    /// <summary>
    /// Working directory for the game server.
    /// </summary>
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Environment variables to set for the game server.
    /// </summary>
    [JsonPropertyName("environmentVariables")]
    public Dictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>
    /// Whether to capture stdout.
    /// </summary>
    [JsonPropertyName("captureStdout")]
    public bool CaptureStdout { get; init; } = true;

    /// <summary>
    /// Whether to capture stderr.
    /// </summary>
    [JsonPropertyName("captureStderr")]
    public bool CaptureStderr { get; init; } = true;

    /// <summary>
    /// Whether to redirect stdin (allows sending commands).
    /// </summary>
    [JsonPropertyName("redirectStdin")]
    public bool RedirectStdin { get; init; } = true;

    /// <summary>
    /// Whether to auto-restart on unexpected exit.
    /// </summary>
    [JsonPropertyName("autoRestart")]
    public bool AutoRestart { get; init; }

    /// <summary>
    /// Maximum restart attempts before giving up.
    /// </summary>
    [JsonPropertyName("maxRestartAttempts")]
    public int MaxRestartAttempts { get; init; } = 3;

    /// <summary>
    /// Delay between restart attempts in seconds.
    /// </summary>
    [JsonPropertyName("restartDelaySeconds")]
    public int RestartDelaySeconds { get; init; } = 5;

    /// <summary>
    /// CPU limit as percentage (0 = no limit).
    /// </summary>
    [JsonPropertyName("cpuLimitPercent")]
    public int CpuLimitPercent { get; init; }

    /// <summary>
    /// Memory limit in megabytes (0 = no limit).
    /// </summary>
    [JsonPropertyName("memoryLimitMb")]
    public int MemoryLimitMb { get; init; }

    /// <summary>
    /// Graceful shutdown timeout in seconds.
    /// </summary>
    [JsonPropertyName("gracefulShutdownTimeoutSeconds")]
    public int GracefulShutdownTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <returns>List of validation errors, or empty if valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            errors.Add("ExecutablePath is required");
        }
        else if (!Path.IsPathRooted(ExecutablePath))
        {
            errors.Add("ExecutablePath must be an absolute path");
        }
        else
        {
            // Path traversal validation using normalization
            var (normalizedPath, isValid) = TryGetFullPath(ExecutablePath);
            if (!isValid)
            {
                errors.Add("ExecutablePath is not a valid path");
            }
            else if (!NormalizedPathsMatch(ExecutablePath, normalizedPath!))
            {
                errors.Add("ExecutablePath contains path traversal sequences");
            }
            else if (!File.Exists(ExecutablePath))
            {
                errors.Add($"Executable not found: {ExecutablePath}");
            }
        }

        if (!string.IsNullOrEmpty(WorkingDirectory))
        {
            if (!Path.IsPathRooted(WorkingDirectory))
            {
                errors.Add("WorkingDirectory must be an absolute path");
            }
            else
            {
                // Path traversal validation using normalization
                var (normalizedPath, isValid) = TryGetFullPath(WorkingDirectory);
                if (!isValid)
                {
                    errors.Add("WorkingDirectory is not a valid path");
                }
                else if (!NormalizedPathsMatch(WorkingDirectory, normalizedPath!))
                {
                    errors.Add("WorkingDirectory contains path traversal sequences");
                }
                else if (!Directory.Exists(WorkingDirectory))
                {
                    errors.Add($"Working directory not found: {WorkingDirectory}");
                }
            }
        }

        if (MaxRestartAttempts < 0)
        {
            errors.Add("MaxRestartAttempts must be non-negative");
        }

        if (RestartDelaySeconds < 1)
        {
            errors.Add("RestartDelaySeconds must be at least 1");
        }

        if (CpuLimitPercent < 0 || CpuLimitPercent > 100)
        {
            errors.Add("CpuLimitPercent must be between 0 and 100");
        }

        if (MemoryLimitMb < 0)
        {
            errors.Add("MemoryLimitMb must be non-negative");
        }

        if (GracefulShutdownTimeoutSeconds < 1)
        {
            errors.Add("GracefulShutdownTimeoutSeconds must be at least 1");
        }

        return errors;
    }

    /// <summary>
    /// JSON serialization options.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32
    };

    /// <summary>
    /// Loads configuration from a file.
    /// </summary>
    /// <param name="path">Path to the configuration file.</param>
    /// <returns>Result containing loaded configuration or error.</returns>
    public static Result<ServerConfig> LoadFromFile(string path)
    {
        try
        {
            // Validate and normalize path before any I/O operations
            var (normalizedPath, isValid) = TryGetFullPath(path);
            if (!isValid || normalizedPath is null)
            {
                return Result<ServerConfig>.Failure($"Invalid configuration file path: {path}");
            }

            // Reject paths with traversal sequences (comparing normalized to input)
            if (!NormalizedPathsMatch(path, normalizedPath))
            {
                return Result<ServerConfig>.Failure("Path traversal detected in configuration file path");
            }

            if (!File.Exists(normalizedPath))
            {
                return Result<ServerConfig>.Failure($"Configuration file not found: {normalizedPath}");
            }

            // Check file size before reading to prevent DoS
            var fileInfo = new FileInfo(normalizedPath);
            if (fileInfo.Length > MaxConfigFileSizeBytes)
            {
                var maxSizeKb = (MaxConfigFileSizeBytes / 1024).ToString(CultureInfo.InvariantCulture);
                return Result<ServerConfig>.Failure($"Configuration file exceeds maximum size of {maxSizeKb}KB");
            }

            var json = File.ReadAllText(normalizedPath);
            var config = JsonSerializer.Deserialize<ServerConfig>(json, JsonOptions);

            if (config is null)
            {
                return Result<ServerConfig>.Failure("Failed to deserialize configuration");
            }

            var errors = config.Validate();
            if (errors.Count > 0)
            {
                return Result<ServerConfig>.Failure(string.Join("; ", errors));
            }

            return Result<ServerConfig>.Success(config);
        }
        catch (JsonException ex)
        {
            return Result<ServerConfig>.Failure($"Invalid JSON in configuration file: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<ServerConfig>.Failure($"Failed to read configuration file: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves configuration to a file.
    /// </summary>
    /// <param name="path">Path to save to.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result SaveToFile(string path)
    {
        // Validate and normalize path before any I/O operations
        var (normalizedPath, isValid) = TryGetFullPath(path);
        if (!isValid || normalizedPath is null)
        {
            return Result.Failure($"Invalid configuration file path: {path}");
        }

        // Reject paths with traversal sequences (comparing normalized to input)
        if (!NormalizedPathsMatch(path, normalizedPath))
        {
            return Result.Failure("Path traversal detected in configuration file path");
        }

        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(normalizedPath, json);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure($"Failed to write configuration file: {ex.Message}");
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

    /// <summary>
    /// Compares paths accounting for trailing separators and OS case sensitivity.
    /// </summary>
    private static bool NormalizedPathsMatch(string original, string normalized)
    {
        // Use OS-aware comparison: case-insensitive on Windows, case-sensitive on Unix
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var originalTrimmed = original.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedTrimmed = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return originalTrimmed.Equals(normalizedTrimmed, comparison);
    }
}
