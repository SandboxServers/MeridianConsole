namespace Dhadgar.Contracts.Agents;

/// <summary>
/// A signed command sent from Nodes to the agent.
/// </summary>
/// <remarks>
/// The agent accepts a command only when all of these hold: <see cref="SigningKeyId"/> equals the id of the key pinned
/// at enrollment (D-09); the signature verifies over the signing input; <see cref="NodeId"/> equals the agent's own node id
/// and <see cref="OrganizationId"/> equals the enrolled organization; <see cref="ExpiresAt"/> minus <see cref="IssuedAt"/>
/// is at most <see cref="AgentMessageLimits.MaxCommandLifetimeSeconds"/>; <see cref="IssuedAt"/> is not later than now plus
/// <see cref="AgentMessageLimits.MaxClockSkewSeconds"/>; and now is before <see cref="ExpiresAt"/>. Otherwise the agent
/// answers <c>Rejected</c>. The dedupe cache must retain <see cref="CommandId"/> values only up to
/// <see cref="ExpiresAt"/> plus the clock skew. Its key is <see cref="CommandId"/>, never the signature, because an ECDSA
/// signature can be re-encoded into a second valid signature over the same input.
/// </remarks>
/// <param name="CommandId">Unique id of the command.</param>
/// <param name="CommandType">One of <see cref="AgentCommandTypes"/>.</param>
/// <param name="NodeId">Target node. Must equal the agent's own node id, or the command is <c>Rejected</c>.</param>
/// <param name="OrganizationId">Owning organization. Must equal the agent's enrolled organization, or the command is <c>Rejected</c>.</param>
/// <param name="InitiatedByUserId">User who requested the command, if any.</param>
/// <param name="IssuedAt">When the command was issued.</param>
/// <param name="ExpiresAt">After this instant the agent must reject the command. Lifetime is at most <see cref="AgentMessageLimits.MaxCommandLifetimeSeconds"/> seconds from <see cref="IssuedAt"/>.</param>
/// <param name="PayloadJson">Command payload serialized with <see cref="AgentJson"/>, at most <see cref="AgentMessageLimits.MaxPayloadJsonBytes"/> UTF-8 bytes. Verify the signature over this raw string before parsing it.</param>
/// <param name="Signature">Base64 ECDSA P-256 SHA-256 signature, IEEE P1363 (64 bytes), over <see cref="AgentCommandSigning.BuildSigningInput(AgentCommandEnvelope)"/>. It must decode to exactly 64 bytes.</param>
/// <param name="SigningKeyId">Key id from <see cref="AgentCommandSigning.ComputeKeyId"/> of the signing key's SPKI. Informational only. The agent verifies only with the key pinned at enrollment (D-09) and must reject a command whose <c>SigningKeyId</c> does not equal the pinned key's id. It must not select or fetch a key by this value.</param>
/// <param name="CorrelationId">Optional caller correlation id, at most <see cref="AgentMessageLimits.MaxCorrelationIdLength"/> characters.</param>
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
/// Result of a command, sent from the agent to Nodes. Not signed; authenticated by mTLS only.
/// </summary>
/// <param name="CommandId">The command this result belongs to.</param>
/// <param name="NodeId">Node that executed the command. Nodes must check it against the certificate claim.</param>
/// <param name="Status">Terminal status.</param>
/// <param name="StartedAt">When execution started.</param>
/// <param name="CompletedAt">When execution finished.</param>
/// <param name="ResultJson">Result payload serialized with <see cref="AgentJson"/>, at most <see cref="AgentMessageLimits.MaxResultJsonBytes"/> UTF-8 bytes, if any.</param>
/// <param name="ErrorMessage">Human-readable error, at most <see cref="AgentMessageLimits.MaxErrorMessageLength"/> characters, if any.</param>
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
/// <param name="AgentVersion">Agent build version, at most <see cref="AgentMessageLimits.MaxAgentVersionLength"/> characters.</param>
/// <param name="Timestamp">When the report was taken.</param>
/// <param name="CpuUsagePercent">Node CPU usage, 0 to 100.</param>
/// <param name="MemoryUsagePercent">Node memory usage, 0 to 100.</param>
/// <param name="DiskUsagePercent">Highest disk usage, 0 to 100.</param>
/// <param name="Servers">Server processes on the node, at most <see cref="AgentMessageLimits.MaxServersPerHeartbeat"/> entries.</param>
/// <param name="Warnings">Non-fatal warnings, at most <see cref="AgentMessageLimits.MaxHeartbeatWarnings"/>, each at most <see cref="AgentMessageLimits.MaxWarningLength"/> characters.</param>
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
/// <param name="Message">Optional detail, bounded by <see cref="AgentMessageLimits.MaxErrorMessageLength"/> characters.</param>
/// <param name="Timestamp">When the change happened.</param>
public sealed record AgentServerStatus(
    Guid NodeId, Guid ServerId, AgentServerState State, int? ExitCode, string? Message, DateTimeOffset Timestamp);

/// <summary>
/// One line of server console output.
/// </summary>
/// <remarks>
/// <see cref="Text"/> is untrusted. It is raw server process output and can include player-controlled chat, ANSI or OSC
/// escape sequences and HTML. Consumers must encode it for the output context (HTML-encode in the UI, and strip or
/// neutralize control and escape sequences before logging).
/// </remarks>
/// <param name="Timestamp">When the line was read.</param>
/// <param name="IsError">True when the line came from stderr.</param>
/// <param name="Text">Untrusted line text, at most <see cref="AgentMessageLimits.MaxConsoleLineLength"/> characters.</param>
public sealed record AgentConsoleLine(DateTimeOffset Timestamp, bool IsError, string Text);

/// <summary>
/// A batch of console lines for one server.
/// </summary>
/// <param name="NodeId">Node the server runs on.</param>
/// <param name="ServerId">Server id.</param>
/// <param name="Lines">Lines, at most <see cref="AgentMessageLimits.MaxConsoleLinesPerBatch"/>, with a total of at most <see cref="AgentMessageLimits.MaxConsoleBatchChars"/> characters. Each line's text is untrusted.</param>
public sealed record AgentConsoleBatch(Guid NodeId, Guid ServerId, IReadOnlyList<AgentConsoleLine> Lines);

/// <summary>
/// Metrics report from the agent.
/// </summary>
/// <param name="NodeId">Reporting node.</param>
/// <param name="Timestamp">When the metrics were sampled.</param>
/// <param name="Metrics">Metric values by name, at most <see cref="AgentMessageLimits.MaxTelemetryMetrics"/> entries. Each name is at most <see cref="AgentMessageLimits.MaxMetricNameLength"/> characters.</param>
public sealed record AgentTelemetry(Guid NodeId, DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Metrics);
