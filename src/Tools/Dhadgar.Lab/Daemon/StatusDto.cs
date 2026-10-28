namespace Dhadgar.Lab.Daemon;

/// <summary>
/// Body of <c>GET /status</c>. Lease fields never carry the lease id.
/// </summary>
public sealed record StatusDto(
    string Version, int Pid, DateTimeOffset StartedAt,
    LeaseStatusDto Lease,
    string StackState,                       // "down" | "starting" | "up" | "degraded"
    IReadOnlyList<ProcessStatusDto> Processes,
    int ActiveFaults,                        // 0 until L19
    IReadOnlyList<string> LeasedMembers,     // empty until L30
    string? RunningJob);                     // job id, null until L27

/// <summary>
/// Lease state as reported over HTTP. Never the lease id.
/// </summary>
public sealed record LeaseStatusDto(bool Held, string? Owner, string? Purpose, DateTimeOffset? Since, DateTimeOffset? ExpiresAt);

/// <summary>
/// One supervised process as reported over HTTP.
/// </summary>
public sealed record ProcessStatusDto(string Name, string State, int? Pid, int Restarts, DateTimeOffset? StartedAt);
