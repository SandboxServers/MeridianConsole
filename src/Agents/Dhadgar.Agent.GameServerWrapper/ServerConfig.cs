using System.Text.Json;
using System.Text.Json.Serialization;

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
        else if (!File.Exists(ExecutablePath))
        {
            errors.Add($"Executable not found: {ExecutablePath}");
        }

        if (!string.IsNullOrEmpty(WorkingDirectory))
        {
            if (!Path.IsPathRooted(WorkingDirectory))
            {
                errors.Add("WorkingDirectory must be an absolute path");
            }
            else if (!Directory.Exists(WorkingDirectory))
            {
                errors.Add($"Working directory not found: {WorkingDirectory}");
            }
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
    /// <returns>Loaded configuration or null if loading failed.</returns>
    public static (ServerConfig? Config, string? Error) LoadFromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return (null, $"Configuration file not found: {path}");
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ServerConfig>(json, JsonOptions);

            if (config is null)
            {
                return (null, "Failed to deserialize configuration");
            }

            var errors = config.Validate();
            if (errors.Count > 0)
            {
                return (null, string.Join("; ", errors));
            }

            return (config, null);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid JSON in configuration file: {ex.Message}");
        }
        catch (IOException ex)
        {
            return (null, $"Failed to read configuration file: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves configuration to a file.
    /// </summary>
    /// <param name="path">Path to save to.</param>
    public void SaveToFile(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }
}
