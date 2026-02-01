namespace Dhadgar.Contracts.Servers;

// Request DTOs for the Servers API

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

public record CreateServerPortRequest(
    string Name,
    string Protocol,
    int InternalPort,
    int? ExternalPort,
    bool IsPrimary);

public record UpdateServerRequest(
    string? Name,
    string? DisplayName,
    IReadOnlyList<string>? Tags);

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

public record UpdateServerResourcesRequest(
    int? CpuLimitMillicores,
    int? MemoryLimitMb,
    int? DiskLimitMb);

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
