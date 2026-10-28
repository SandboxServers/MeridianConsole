using Dhadgar.Contracts.Agents;

namespace Dhadgar.Contracts.Nodes;

/// <summary>Names shared by the agent and Nodes for the agent hub (ADR-0008).</summary>
public static class AgentHubProtocol
{
    /// <summary>Hub path. Identical at the Gateway and in Nodes (no prefix transform).</summary>
    public const string Path = "/hubs/agent";

    /// <summary>Request header carrying the hub protocol version (#116).</summary>
    public const string VersionHeader = "X-Agent-Protocol";

    /// <summary>
    /// The only version Nodes accepts. A missing header is treated as this value.
    /// Adding a field that an older receiver must not ignore (a new restriction, limit or safety setting) requires a new version. Until Nodes accepts several versions and records the announced version per connection, a bump refuses older agents. Nodes sends such a field only to connections that announced the new version (D-40).
    /// </summary>
    public const string CurrentVersion = "1";

    /// <summary>Claim that carries the node ID taken from the client certificate's SPIFFE ID.</summary>
    public const string NodeIdClaim = "node_id";

    /// <summary>SignalR group holding every connection of one node.</summary>
    public static string NodeGroup(Guid nodeId) => $"node:{nodeId:D}";

    /// <summary>
    /// Size in bytes of one SignalR JSON hub frame AFTER escaping. Nodes sets HubOptions.MaximumReceiveMessageSize
    /// to this value. The limits in <see cref="AgentMessageLimits"/> count unescaped characters or UTF-8 bytes, and a
    /// control or non-ASCII character escapes to 6 bytes (\uXXXX), so 64 KiB of content can be 384 KiB on the wire.
    /// </summary>
    public const int MaxReceiveMessageBytes = 512 * 1024;
}

/// <summary>Values of <see cref="AgentHeartbeatAck.ErrorCode"/> and <see cref="AgentHubAck.ErrorCode"/>.</summary>
public static class AgentHubErrorCodes
{
    /// <summary>Sprint 2: the node id is unknown.</summary>
    public const string NodeNotFound = "node_not_found";

    /// <summary>Sprint 2: the node is decommissioned.</summary>
    public const string NodeDecommissioned = "node_decommissioned";

    /// <summary>Sprint 2: the heartbeat failed validation.</summary>
    public const string InvalidHeartbeat = "invalid_heartbeat";

    /// <summary>Sprint 2: the heartbeat could not be processed.</summary>
    public const string HeartbeatFailed = "heartbeat_failed";

    /// <summary>VS-03R: the telemetry failed validation.</summary>
    public const string InvalidTelemetry = "invalid_telemetry";

    /// <summary>VS-03R: the command result failed validation.</summary>
    public const string InvalidResult = "invalid_result";

    /// <summary>VS-03R: the server status failed validation.</summary>
    public const string InvalidStatus = "invalid_status";

    /// <summary>VS-03R: the console batch failed validation.</summary>
    public const string InvalidConsoleBatch = "invalid_console_batch";

    /// <summary>VS-03R: the command result names a command id that is unknown or belongs to another node.</summary>
    public const string UnknownCommand = "unknown_command";

    /// <summary>VS-03R: the server is not on this node under a claimed capacity reservation.</summary>
    public const string ServerNotOnNode = "server_not_on_node";

    /// <summary>VS-03R: stub answer until the hub method is built.</summary>
    public const string NotSupported = "not_supported";
}

/// <summary>Limits Nodes enforces on <see cref="AgentHeartbeat"/>. The agent clamps to them before sending.</summary>
public static class AgentHeartbeatLimits
{
    /// <summary>Maximum length of <see cref="AgentHeartbeat.AgentVersion"/>. Also bounds <see cref="PingResult.AgentVersion"/>.</summary>
    public const int MaxAgentVersionLength = 50;

    /// <summary>Maximum number of <see cref="AgentHeartbeat.HealthIssues"/> entries.</summary>
    public const int MaxHealthIssues = 20;

    /// <summary>Maximum length of one health issue.</summary>
    public const int MaxHealthIssueLength = 256;

    /// <summary>Maximum <see cref="AgentHeartbeat.ActiveGameServers"/>.</summary>
    public const int MaxActiveGameServers = 10_000;
}

/// <summary>Server methods the agent invokes. Implemented by Nodes' AgentHub.</summary>
/// <remarks>
/// Telemetry, CommandResult, ServerStatusChanged and ConsoleOutput answer <see cref="AgentHubErrorCodes.NotSupported"/>
/// until VS-08R (Telemetry) and VS-33 (the rest) implement them.
/// </remarks>
public interface IAgentHub
{
    /// <summary>Reports node health. The node is identified by the connection's certificate, never by the payload.</summary>
    Task<AgentHeartbeatAck> Heartbeat(AgentHeartbeat heartbeat);

    /// <summary>Periodic metrics report. Business failures are acks, never a <c>HubException</c> (D-28).</summary>
    Task<AgentHubAck> Telemetry(AgentTelemetry telemetry);

    /// <summary>Outcome of a previously received command. The node is taken from the certificate (D-27).</summary>
    Task<AgentHubAck> CommandResult(AgentCommandResult result);

    /// <summary>A server process changed state. The node is taken from the certificate (D-27).</summary>
    Task<AgentHubAck> ServerStatusChanged(AgentServerStatus status);

    /// <summary>A batch of server console lines. The node is taken from the certificate (D-27).</summary>
    Task<AgentHubAck> ConsoleOutput(AgentConsoleBatch batch);
}

/// <summary>Client methods Nodes invokes on the agent.</summary>
public interface IAgentHubClient
{
    /// <summary>Delivers a signed command to the agent. The agent verifies the signature before it acts (D-09).</summary>
    Task ReceiveCommand(AgentCommandEnvelope command);

    /// <summary>
    /// Liveness check carrying an opaque nonce echoed back in the ping result. The nonce is at most
    /// <see cref="AgentMessageLimits.MaxNonceLength"/> characters.
    /// </summary>
    Task ReceivePing(string message);
}

/// <summary>One heartbeat. Percentages are 0-100.</summary>
public sealed record AgentHeartbeat(
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    int ActiveGameServers,
    string? AgentVersion,
    IReadOnlyList<string>? HealthIssues);

/// <summary>Reply to <see cref="IAgentHub.Heartbeat"/>. On failure, Acknowledged is false and ErrorCode is one of <see cref="AgentHubErrorCodes"/>.</summary>
public sealed record AgentHeartbeatAck(
    bool Acknowledged,
    DateTimeOffset ServerTime,
    string? ErrorCode = null);

/// <summary>
/// Business result of every other agent to Nodes hub call (D-28). On failure, Accepted is false and ErrorCode is one of
/// <see cref="AgentHubErrorCodes"/>. A business failure is never thrown as a <c>HubException</c>.
/// </summary>
public sealed record AgentHubAck(bool Accepted, string? ErrorCode = null);
