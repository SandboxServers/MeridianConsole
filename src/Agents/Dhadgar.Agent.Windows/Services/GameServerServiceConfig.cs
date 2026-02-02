using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Dhadgar.Agent.Windows.Services;

/// <summary>
/// Configuration for a game server Windows Service.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This configuration controls how game server services are created.
/// Service names must be validated to prevent injection attacks.
/// All paths must be absolute and validated before use.
/// </remarks>
public sealed partial class GameServerServiceConfig : IValidatableObject
{
    /// <summary>
    /// Maximum length for service names (Windows limit is 256).
    /// </summary>
    public const int MaxServiceNameLength = 256;

    /// <summary>
    /// Prefix for all game server service names.
    /// </summary>
    public const string ServiceNamePrefix = "MeridianGS_";

    /// <summary>
    /// Pattern for valid server IDs (alphanumeric, hyphen, underscore).
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled)]
    private static partial Regex ServerIdPattern();

    /// <summary>
    /// Pattern for valid pipe names: MeridianAgent_{agentId}\{serverId}
    /// Agent ID must be a 32-char hex GUID without hyphens, server ID alphanumeric.
    /// </summary>
    [GeneratedRegex(@"^MeridianAgent_[a-f0-9]{32}\\[a-zA-Z0-9\-_]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PipeNamePattern();

    /// <summary>
    /// The unique server identifier from the control plane.
    /// Must be alphanumeric with hyphens/underscores only.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(200)] // Leave room for prefix
    public required string ServerId { get; init; }

    /// <summary>
    /// The GUID-based process identifier for internal tracking.
    /// </summary>
    public required Guid ProcessId { get; init; }

    /// <summary>
    /// Path to the GameServerWrapper executable.
    /// Must be an absolute path.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string WrapperExecutablePath { get; init; }

    /// <summary>
    /// Directory where the game server files are located.
    /// This directory will have ACLs configured for the service account.
    /// Must be an absolute path.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string ServerDirectory { get; init; }

    /// <summary>
    /// Path to the game server configuration file.
    /// The wrapper reads this to know how to launch the actual game server.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string ConfigFilePath { get; init; }

    /// <summary>
    /// Name of the named pipe for IPC communication.
    /// Format: MeridianAgent_{agentId}\{serverId}
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string PipeName { get; init; }

    /// <summary>
    /// Display name shown in Windows Services management.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Description shown in Windows Services management.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the Windows Service name for this game server.
    /// Format: MeridianGS_{serverId}
    /// </summary>
    public string ServiceName => $"{ServiceNamePrefix}{ServerId}";

    /// <summary>
    /// Gets the Virtual Service Account name for this service.
    /// Format: NT SERVICE\MeridianGS_{serverId}
    /// </summary>
    public string ServiceAccountName => $@"NT SERVICE\{ServiceName}";

    /// <summary>
    /// Gets the service account SID format.
    /// </summary>
    public string ServiceAccountIdentity => ServiceAccountName;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate ServerId format
        if (!string.IsNullOrEmpty(ServerId))
        {
            if (!ServerIdPattern().IsMatch(ServerId))
            {
                yield return new ValidationResult(
                    "ServerId must contain only alphanumeric characters, hyphens, and underscores",
                    [nameof(ServerId)]);
            }

            // Check combined service name length
            if (ServiceName.Length > MaxServiceNameLength)
            {
                yield return new ValidationResult(
                    $"Combined service name '{ServiceName}' exceeds maximum length of {MaxServiceNameLength}",
                    [nameof(ServerId)]);
            }
        }

        // Validate WrapperExecutablePath is absolute and normalized
        if (!string.IsNullOrEmpty(WrapperExecutablePath))
        {
            if (!Path.IsPathRooted(WrapperExecutablePath))
            {
                yield return new ValidationResult(
                    "WrapperExecutablePath must be an absolute path",
                    [nameof(WrapperExecutablePath)]);
            }
            else if (!IsNormalizedPath(WrapperExecutablePath))
            {
                yield return new ValidationResult(
                    "WrapperExecutablePath contains path traversal sequences",
                    [nameof(WrapperExecutablePath)]);
            }
        }

        // Validate ServerDirectory is absolute and normalized
        if (!string.IsNullOrEmpty(ServerDirectory))
        {
            if (!Path.IsPathRooted(ServerDirectory))
            {
                yield return new ValidationResult(
                    "ServerDirectory must be an absolute path",
                    [nameof(ServerDirectory)]);
            }
            else if (!IsNormalizedPath(ServerDirectory))
            {
                yield return new ValidationResult(
                    "ServerDirectory contains path traversal sequences",
                    [nameof(ServerDirectory)]);
            }
        }

        // Validate ConfigFilePath is absolute and normalized
        if (!string.IsNullOrEmpty(ConfigFilePath))
        {
            if (!Path.IsPathRooted(ConfigFilePath))
            {
                yield return new ValidationResult(
                    "ConfigFilePath must be an absolute path",
                    [nameof(ConfigFilePath)]);
            }
            else if (!IsNormalizedPath(ConfigFilePath))
            {
                yield return new ValidationResult(
                    "ConfigFilePath contains path traversal sequences",
                    [nameof(ConfigFilePath)]);
            }
        }

        // Validate PipeName format with strict regex
        if (!string.IsNullOrEmpty(PipeName))
        {
            if (!PipeNamePattern().IsMatch(PipeName))
            {
                yield return new ValidationResult(
                    "PipeName must match format 'MeridianAgent_{guid}\\{serverId}' with alphanumeric IDs",
                    [nameof(PipeName)]);
            }
        }
    }

    /// <summary>
    /// Checks if a path is normalized (no traversal sequences).
    /// </summary>
    private static bool IsNormalizedPath(string path)
    {
        try
        {
            var normalized = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var originalTrimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedTrimmed = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return originalTrimmed.Equals(normalizedTrimmed, comparison);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a validated service configuration.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="processId">The internal process GUID.</param>
    /// <param name="wrapperPath">Path to the GameServerWrapper executable.</param>
    /// <param name="serverDirectory">Directory containing game server files.</param>
    /// <param name="configPath">Path to the server configuration file.</param>
    /// <param name="agentId">The agent's node ID for pipe naming.</param>
    /// <returns>A validated configuration or null if validation fails.</returns>
    public static GameServerServiceConfig? Create(
        string serverId,
        Guid processId,
        string wrapperPath,
        string serverDirectory,
        string configPath,
        Guid agentId)
    {
        var config = new GameServerServiceConfig
        {
            ServerId = serverId,
            ProcessId = processId,
            WrapperExecutablePath = wrapperPath,
            ServerDirectory = serverDirectory,
            ConfigFilePath = configPath,
            PipeName = $@"MeridianAgent_{agentId:N}\{serverId}",
            DisplayName = $"Meridian Game Server - {serverId}",
            Description = $"Managed game server instance for Meridian Console (Server ID: {serverId})"
        };

        // Validate the configuration
        var validationContext = new ValidationContext(config);
        var validationResults = new List<ValidationResult>();

        if (!Validator.TryValidateObject(config, validationContext, validationResults, validateAllProperties: true))
        {
            return null;
        }

        return config;
    }
}

/// <summary>
/// Status information for a game server Windows Service.
/// </summary>
public sealed class ServiceInfo
{
    /// <summary>
    /// The Windows Service name.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// The current service status.
    /// </summary>
    public required ServiceStatus Status { get; init; }

    /// <summary>
    /// The Virtual Service Account name.
    /// </summary>
    public required string ServiceAccountName { get; init; }

    /// <summary>
    /// The process ID of the service if running.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// The server directory with ACLs configured.
    /// </summary>
    public string? ServerDirectory { get; init; }

    /// <summary>
    /// When the service was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>
/// Status of a Windows Service.
/// </summary>
public enum ServiceStatus
{
    /// <summary>
    /// Service does not exist.
    /// </summary>
    NotInstalled,

    /// <summary>
    /// Service is stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// Service is starting.
    /// </summary>
    Starting,

    /// <summary>
    /// Service is running.
    /// </summary>
    Running,

    /// <summary>
    /// Service is stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// Service is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Service status is unknown.
    /// </summary>
    Unknown
}
