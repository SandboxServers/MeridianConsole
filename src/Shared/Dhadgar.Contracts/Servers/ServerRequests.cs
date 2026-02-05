namespace Dhadgar.Contracts.Servers;

/// <summary>
/// Request to create a new game server.
/// </summary>
/// <param name="Name">Unique server name within the organization.</param>
/// <param name="DisplayName">Optional display name shown in the UI.</param>
/// <param name="GameType">Game type identifier (e.g., "minecraft", "valheim").</param>
/// <param name="CpuLimitMillicores">CPU limit in millicores (1000 = 1 core).</param>
/// <param name="MemoryLimitMb">Memory limit in megabytes.</param>
/// <param name="DiskLimitMb">Disk space limit in megabytes.</param>
/// <param name="TemplateId">Optional template ID to use for default settings.</param>
/// <param name="StartupCommand">Custom startup command (overrides template).</param>
/// <param name="GameSettings">Game-specific configuration settings.</param>
/// <param name="AutoStart">Whether to start the server automatically after creation.</param>
/// <param name="AutoRestartOnCrash">Whether to restart the server automatically if it crashes.</param>
/// <param name="Ports">Port allocations for the server.</param>
/// <param name="Tags">Optional tags for organization and filtering.</param>
public record CreateServerRequest(
    string Name,
    string? DisplayName,
    string GameType,
    int CpuLimitMillicores,
    int MemoryLimitMb,
    int DiskLimitMb,
    Guid? TemplateId,
    string? StartupCommand,
    Dictionary<string, object>? GameSettings,
    bool AutoStart,
    bool AutoRestartOnCrash,
    IReadOnlyList<CreateServerPortRequest>? Ports,
    IReadOnlyList<string>? Tags);

/// <summary>
/// Request to create a port allocation for a server.
/// </summary>
/// <param name="Name">Descriptive name for the port (e.g., "game", "rcon", "query").</param>
/// <param name="Protocol">Protocol type: "tcp" or "udp".</param>
/// <param name="InternalPort">Port number inside the container.</param>
/// <param name="ExternalPort">Optional external port (auto-assigned if null).</param>
/// <param name="IsPrimary">Whether this is the primary game port.</param>
public record CreateServerPortRequest(
    string Name,
    string Protocol,
    int InternalPort,
    int? ExternalPort,
    bool IsPrimary);

/// <summary>
/// Request to update basic server metadata.
/// </summary>
/// <param name="Name">New server name (must be unique within organization).</param>
/// <param name="DisplayName">New display name.</param>
/// <param name="Tags">New tags list (replaces existing tags).</param>
public record UpdateServerRequest(
    string? Name,
    string? DisplayName,
    IReadOnlyList<string>? Tags);

/// <summary>
/// Request to update server configuration settings.
/// </summary>
/// <param name="StartupCommand">Custom startup command.</param>
/// <param name="GameSettings">Game-specific configuration settings.</param>
/// <param name="EnvironmentVariables">Environment variables for the server process.</param>
/// <param name="AutoStart">Whether to start the server automatically.</param>
/// <param name="AutoRestartOnCrash">Whether to restart automatically after crashes.</param>
/// <param name="MaxAutoRestartAttempts">Maximum restart attempts before giving up.</param>
/// <param name="AutoRestartCooldownSeconds">Cooldown period between restart attempts.</param>
/// <param name="AutoRestartDelaySeconds">Delay before attempting restart.</param>
/// <param name="ShutdownTimeoutSeconds">Timeout for graceful shutdown.</param>
/// <param name="JavaFlags">Java-specific flags for JVM-based servers.</param>
public record UpdateServerConfigurationRequest(
    string? StartupCommand,
    Dictionary<string, object>? GameSettings,
    Dictionary<string, string>? EnvironmentVariables,
    bool? AutoStart,
    bool? AutoRestartOnCrash,
    int? MaxAutoRestartAttempts,
    int? AutoRestartCooldownSeconds,
    int? AutoRestartDelaySeconds,
    int? ShutdownTimeoutSeconds,
    string? JavaFlags);

/// <summary>
/// Request to update server resource limits.
/// </summary>
/// <param name="CpuLimitMillicores">CPU limit in millicores (1000 = 1 core).</param>
/// <param name="MemoryLimitMb">Memory limit in megabytes.</param>
/// <param name="DiskLimitMb">Disk space limit in megabytes.</param>
public record UpdateServerResourcesRequest(
    int? CpuLimitMillicores,
    int? MemoryLimitMb,
    int? DiskLimitMb);

/// <summary>
/// Request to create a new server template.
/// </summary>
/// <param name="Name">Template name.</param>
/// <param name="Description">Optional description of the template.</param>
/// <param name="GameType">Game type this template applies to.</param>
/// <param name="IsPublic">Whether this template is visible to other organizations.</param>
/// <param name="DefaultCpuLimitMillicores">Default CPU limit in millicores.</param>
/// <param name="DefaultMemoryLimitMb">Default memory limit in megabytes.</param>
/// <param name="DefaultDiskLimitMb">Default disk space limit in megabytes.</param>
/// <param name="DefaultStartupCommand">Default startup command.</param>
/// <param name="DefaultGameSettings">Default game-specific settings.</param>
/// <param name="DefaultEnvironmentVariables">Default environment variables.</param>
/// <param name="DefaultJavaFlags">Default Java flags for JVM-based servers.</param>
/// <param name="DefaultPorts">Default port allocations.</param>
public record CreateServerTemplateRequest(
    string Name,
    string? Description,
    string GameType,
    bool IsPublic,
    int DefaultCpuLimitMillicores,
    int DefaultMemoryLimitMb,
    int DefaultDiskLimitMb,
    string? DefaultStartupCommand,
    Dictionary<string, object>? DefaultGameSettings,
    Dictionary<string, string>? DefaultEnvironmentVariables,
    string? DefaultJavaFlags,
    IReadOnlyList<CreateServerPortRequest>? DefaultPorts);

/// <summary>
/// Request to update an existing server template.
/// </summary>
public record UpdateServerTemplateRequest(
    string? Name,
    string? Description,
    bool? IsPublic,
    bool? IsArchived,
    int? DefaultCpuLimitMillicores,
    int? DefaultMemoryLimitMb,
    int? DefaultDiskLimitMb,
    string? DefaultStartupCommand,
    Dictionary<string, object>? DefaultGameSettings,
    Dictionary<string, string>? DefaultEnvironmentVariables,
    string? DefaultJavaFlags,
    IReadOnlyList<CreateServerPortRequest>? DefaultPorts);

/// <summary>
/// Query parameters for listing servers with filtering and pagination.
/// </summary>
/// <param name="Page">Page number (1-based).</param>
/// <param name="PageSize">Number of items per page (max 100).</param>
/// <param name="Status">Filter by server status (e.g., "Running", "Stopped").</param>
/// <param name="PowerState">Filter by power state (e.g., "On", "Off").</param>
/// <param name="GameType">Filter by game type.</param>
/// <param name="NodeId">Filter by node ID.</param>
/// <param name="Search">Search by server name or display name.</param>
/// <param name="Tags">Comma-separated list of tags to filter by.</param>
/// <param name="SortBy">Field to sort by: "name", "createdAt", "status".</param>
/// <param name="SortOrder">Sort order: "asc" or "desc".</param>
/// <param name="IncludeDeleted">Include soft-deleted servers in results.</param>
public record ServerListQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    string? PowerState = null,
    string? GameType = null,
    Guid? NodeId = null,
    string? Search = null,
    string? Tags = null,
    string SortBy = "name",
    string SortOrder = "asc",
    bool IncludeDeleted = false);
