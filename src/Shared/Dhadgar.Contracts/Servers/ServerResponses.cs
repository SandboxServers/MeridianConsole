namespace Dhadgar.Contracts.Servers;

// Response DTOs for the Servers API

public record ServerListItem(
    Guid Id,
    string Name,
    string? DisplayName,
    string GameType,
    string Status,
    string PowerState,
    Guid? NodeId,
    int CpuLimitMillicores,
    int MemoryLimitMb,
    int DiskLimitMb,
    DateTime? LastStartedAt,
    DateTime? LastStoppedAt,
    int CrashCount,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ServerDetail(
    Guid Id,
    Guid OrganizationId,
    Guid? NodeId,
    string Name,
    string? DisplayName,
    string GameType,
    string Status,
    string PowerState,
    int CpuLimitMillicores,
    int MemoryLimitMb,
    int DiskLimitMb,
    Guid? ReservationToken,
    DateTime? LastStartedAt,
    DateTime? LastStoppedAt,
    long TotalUptimeSeconds,
    int CrashCount,
    IReadOnlyList<string> Tags,
    ServerConfigurationDto? Configuration,
    Guid? TemplateId,
    IReadOnlyList<ServerPortDto> Ports,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ServerConfigurationDto(
    string? StartupCommand,
    Dictionary<string, object>? GameSettings,
    Dictionary<string, string>? EnvironmentVariables,
    bool AutoStart,
    bool AutoRestartOnCrash,
    int MaxAutoRestartAttempts,
    int AutoRestartCooldownSeconds,
    int AutoRestartDelaySeconds,
    int ShutdownTimeoutSeconds,
    string? JavaFlags);

public record ServerPortDto(
    Guid Id,
    string Name,
    string Protocol,
    int InternalPort,
    int ExternalPort,
    bool IsPrimary);

public record ServerTemplateListItem(
    Guid Id,
    string Name,
    string? Description,
    string GameType,
    bool IsPublic,
    bool IsArchived,
    int UsageCount,
    DateTime CreatedAt);

public record ServerTemplateDetail(
    Guid Id,
    Guid? OrganizationId,
    string Name,
    string? Description,
    string GameType,
    bool IsPublic,
    bool IsArchived,
    int DefaultCpuLimitMillicores,
    int DefaultMemoryLimitMb,
    int DefaultDiskLimitMb,
    string? DefaultStartupCommand,
    Dictionary<string, object>? DefaultGameSettings,
    Dictionary<string, string>? DefaultEnvironmentVariables,
    string? DefaultJavaFlags,
    IReadOnlyList<CreateServerPortRequest>? DefaultPorts,
    int UsageCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
