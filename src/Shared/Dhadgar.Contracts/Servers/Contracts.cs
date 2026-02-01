namespace Dhadgar.Contracts.Servers;

public record ServerId(Guid Value);

public record ServerProvisionRequested(
    Guid ServerId,
    Guid OrgId,
    string GameType,
    int CpuLimit,
    int MemoryMb);

public record ServerProvisioned(
    Guid ServerId,
    Guid OrgId,
    string NodeId,
    IReadOnlyDictionary<string, string> ConnectionInfo);

// Server lifecycle events
public record ServerStarted(
    Guid ServerId,
    Guid OrgId,
    string ServerName,
    string GameType,
    DateTimeOffset OccurredAtUtc);

public record ServerStopped(
    Guid ServerId,
    Guid OrgId,
    string ServerName,
    string Reason,
    DateTimeOffset OccurredAtUtc);

public record ServerCrashed(
    Guid ServerId,
    Guid OrgId,
    string ServerName,
    string ErrorSummary,
    int? ExitCode,
    DateTimeOffset OccurredAtUtc);

public record ServerRestarted(
    Guid ServerId,
    Guid OrgId,
    string ServerName,
    string Reason,
    DateTimeOffset OccurredAtUtc);

public record ServerPlayerCountChanged(
    Guid ServerId,
    Guid OrgId,
    string ServerName,
    int CurrentPlayers,
    int MaxPlayers,
    DateTimeOffset OccurredAtUtc);
