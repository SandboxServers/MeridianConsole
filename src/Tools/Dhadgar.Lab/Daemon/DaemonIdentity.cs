namespace Dhadgar.Lab.Daemon;

/// <summary>
/// Who the running daemon is: its process id and start time. Registered once at start.
/// </summary>
public sealed record DaemonIdentity(int Pid, DateTimeOffset StartedAt);
