namespace Dhadgar.Contracts.Agents;

/// <summary>
/// A signed command sent from Nodes to the agent.
/// </summary>
/// <param name="CommandId">Unique id of the command.</param>
/// <param name="CommandType">One of <see cref="AgentCommandTypes"/>.</param>
/// <param name="NodeId">Target node.</param>
/// <param name="OrganizationId">Owning organization.</param>
/// <param name="InitiatedByUserId">User who requested the command, if any.</param>
/// <param name="IssuedAt">When the command was issued.</param>
/// <param name="ExpiresAt">After this instant the agent must reject the command.</param>
/// <param name="PayloadJson">Command payload serialized with <see cref="AgentJson"/>.</param>
/// <param name="Signature">Base64 ECDSA P-256 SHA-256 signature, IEEE P1363 (64 bytes), over <see cref="AgentCommandSigning.BuildSigningInput(AgentCommandEnvelope)"/>.</param>
/// <param name="SigningKeyId">Key id from <see cref="AgentCommandSigning.ComputeKeyId"/> of the signing key's SPKI.</param>
/// <param name="CorrelationId">Optional caller correlation id.</param>
public sealed record AgentCommandEnvelope(
    Guid CommandId,
    string CommandType,
    Guid NodeId,
    Guid OrganizationId,
    Guid? InitiatedByUserId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string PayloadJson,
    string Signature,
    string SigningKeyId,
    string? CorrelationId);

/// <summary>
/// Terminal status of a command.
/// </summary>
public enum AgentCommandStatus
{
    /// <summary>The command completed successfully.</summary>
    Succeeded = 0,

    /// <summary>The command ran and failed.</summary>
    Failed = 1,

    /// <summary>The agent refused the command (for example a bad signature or expired command).</summary>
    Rejected = 2,

    /// <summary>The command did not complete in time.</summary>
    TimedOut = 3,

    /// <summary>The command was cancelled.</summary>
    Cancelled = 4
}

/// <summary>
/// Result of a command, sent from the agent to Nodes.
/// </summary>
/// <param name="CommandId">The command this result belongs to.</param>
/// <param name="NodeId">Node that executed the command.</param>
/// <param name="Status">Terminal status.</param>
/// <param name="StartedAt">When execution started.</param>
/// <param name="CompletedAt">When execution finished.</param>
/// <param name="ResultJson">Result payload serialized with <see cref="AgentJson"/>, if any.</param>
/// <param name="ErrorMessage">Human-readable error, if any.</param>
/// <param name="ErrorCode">Machine-readable error code, if any.</param>
/// <param name="CorrelationId">Correlation id echoed from the command.</param>
public sealed record AgentCommandResult(
    Guid CommandId,
    Guid NodeId,
    AgentCommandStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? ResultJson,
    string? ErrorMessage,
    string? ErrorCode,
    string? CorrelationId);

/// <summary>
/// Lifecycle state of a game server process.
/// </summary>
public enum AgentServerState
{
    /// <summary>Not running.</summary>
    Stopped = 0,

    /// <summary>Server files are being installed.</summary>
    Installing = 1,

    /// <summary>Process is starting.</summary>
    Starting = 2,

    /// <summary>Process is running.</summary>
    Running = 3,

    /// <summary>Process is stopping.</summary>
    Stopping = 4,

    /// <summary>Process exited unexpectedly.</summary>
    Crashed = 5
}

/// <summary>
/// Per-server process figures reported in a heartbeat.
/// </summary>
/// <param name="ServerId">Server id.</param>
/// <param name="State">Current state.</param>
/// <param name="OsPid">OS process id, when running.</param>
/// <param name="CpuPercent">CPU usage of the process.</param>
/// <param name="MemoryBytes">Resident memory of the process.</param>
/// <param name="UptimeSeconds">Seconds since the process started.</param>
public sealed record AgentServerProcess(
    Guid ServerId, AgentServerState State, int? OsPid, double CpuPercent, long MemoryBytes, long UptimeSeconds);

/// <summary>
/// Periodic health report from the agent.
/// </summary>
/// <param name="NodeId">Reporting node.</param>
/// <param name="AgentVersion">Agent build version.</param>
/// <param name="Timestamp">When the report was taken.</param>
/// <param name="CpuUsagePercent">Node CPU usage, 0 to 100.</param>
/// <param name="MemoryUsagePercent">Node memory usage, 0 to 100.</param>
/// <param name="DiskUsagePercent">Highest disk usage, 0 to 100.</param>
/// <param name="Servers">Server processes on the node.</param>
/// <param name="Warnings">Non-fatal warnings, at most <see cref="AgentMessageLimits.MaxHeartbeatWarnings"/>.</param>
public sealed record AgentHeartbeat(
    Guid NodeId,
    string AgentVersion,
    DateTimeOffset Timestamp,
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    IReadOnlyList<AgentServerProcess> Servers,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A server process changed state.
/// </summary>
/// <param name="NodeId">Node the server runs on.</param>
/// <param name="ServerId">Server id.</param>
/// <param name="State">New state.</param>
/// <param name="ExitCode">Process exit code, when it exited.</param>
/// <param name="Message">Optional detail.</param>
/// <param name="Timestamp">When the change happened.</param>
public sealed record AgentServerStatus(
    Guid NodeId, Guid ServerId, AgentServerState State, int? ExitCode, string? Message, DateTimeOffset Timestamp);

/// <summary>
/// One line of server console output.
/// </summary>
/// <param name="Timestamp">When the line was read.</param>
/// <param name="IsError">True when the line came from stderr.</param>
/// <param name="Text">Line text, at most <see cref="AgentMessageLimits.MaxConsoleLineLength"/> characters.</param>
public sealed record AgentConsoleLine(DateTimeOffset Timestamp, bool IsError, string Text);

/// <summary>
/// A batch of console lines for one server.
/// </summary>
/// <param name="NodeId">Node the server runs on.</param>
/// <param name="ServerId">Server id.</param>
/// <param name="Lines">Lines, at most <see cref="AgentMessageLimits.MaxConsoleLinesPerBatch"/>.</param>
public sealed record AgentConsoleBatch(Guid NodeId, Guid ServerId, IReadOnlyList<AgentConsoleLine> Lines);

/// <summary>
/// Metrics report from the agent.
/// </summary>
/// <param name="NodeId">Reporting node.</param>
/// <param name="Timestamp">When the metrics were sampled.</param>
/// <param name="Metrics">Metric values by name, at most <see cref="AgentMessageLimits.MaxTelemetryMetrics"/> entries.</param>
public sealed record AgentTelemetry(Guid NodeId, DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Metrics);
