namespace Dhadgar.Contracts.Servers;

// Additional server lifecycle events beyond those in Contracts.cs

/// <summary>
/// Published when a new server is created.
/// </summary>
public record ServerCreated(
    Guid ServerId,
    Guid OrganizationId,
    string Name,
    string GameType,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Published when a server is deleted (soft delete).
/// </summary>
public record ServerDeleted(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    DateTimeOffset DeletedAtUtc);

/// <summary>
/// Published when a server's status changes.
/// </summary>
public record ServerStatusChanged(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    string OldStatus,
    string NewStatus,
    string? Reason,
    DateTimeOffset ChangedAtUtc);

/// <summary>
/// Published when a server's power state changes.
/// </summary>
public record ServerPowerStateChanged(
    Guid ServerId,
    Guid OrganizationId,
    string OldPowerState,
    string NewPowerState,
    DateTimeOffset ChangedAtUtc);

/// <summary>
/// Published when a server is placed on a node.
/// </summary>
public record ServerPlaced(
    Guid ServerId,
    Guid OrganizationId,
    Guid NodeId,
    Guid ReservationToken,
    DateTimeOffset PlacedAtUtc);

/// <summary>
/// Published when a server is moved to a different node.
/// </summary>
public record ServerMigrated(
    Guid ServerId,
    Guid OrganizationId,
    Guid OldNodeId,
    Guid NewNodeId,
    string Reason,
    DateTimeOffset MigratedAtUtc);

/// <summary>
/// Published when a server enters maintenance mode.
/// </summary>
public record ServerMaintenanceStarted(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    DateTimeOffset Timestamp);

/// <summary>
/// Published when a server exits maintenance mode.
/// </summary>
public record ServerMaintenanceEnded(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    DateTimeOffset Timestamp);

/// <summary>
/// Published when a server is suspended (billing or admin action).
/// </summary>
public record ServerSuspended(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    string Reason,
    DateTimeOffset Timestamp);

/// <summary>
/// Published when a server is unsuspended.
/// </summary>
public record ServerUnsuspended(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    DateTimeOffset Timestamp);

/// <summary>
/// Published when a server's configuration is updated.
/// </summary>
public record ServerConfigurationUpdated(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    DateTimeOffset Timestamp);

/// <summary>
/// Published when auto-restart is triggered for a crashed server.
/// </summary>
public record ServerAutoRestartTriggered(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    int AttemptNumber,
    int MaxAttempts,
    DateTimeOffset Timestamp);

/// <summary>
/// Published when auto-restart fails after max attempts.
/// </summary>
public record ServerAutoRestartFailed(
    Guid ServerId,
    Guid OrganizationId,
    string ServerName,
    int TotalAttempts,
    DateTimeOffset Timestamp);
