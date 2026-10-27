namespace Dhadgar.Contracts.Nodes;

/// <summary>Names shared by the agent and Nodes for the agent hub (ADR-0008).</summary>
public static class AgentHubProtocol
{
    /// <summary>Hub path. Identical at the Gateway and in Nodes (no prefix transform).</summary>
    public const string Path = "/hubs/agent";

    /// <summary>Request header carrying the hub protocol version (#116).</summary>
    public const string VersionHeader = "X-Agent-Protocol";

    /// <summary>The only version Nodes accepts. A missing header is treated as this value.</summary>
    public const string CurrentVersion = "1";

    /// <summary>Claim that carries the node ID taken from the client certificate's SPIFFE ID.</summary>
    public const string NodeIdClaim = "node_id";

    /// <summary>SignalR group holding every connection of one node.</summary>
    public static string NodeGroup(Guid nodeId) => $"node:{nodeId:D}";
}

/// <summary>Values of <see cref="AgentHeartbeatAck.ErrorCode"/>.</summary>
public static class AgentHubErrorCodes
{
    public const string NodeNotFound = "node_not_found";
    public const string NodeDecommissioned = "node_decommissioned";
    public const string InvalidHeartbeat = "invalid_heartbeat";
    public const string HeartbeatFailed = "heartbeat_failed";
}

/// <summary>Limits Nodes enforces on <see cref="AgentHeartbeat"/>. The agent clamps to them before sending.</summary>
public static class AgentHeartbeatLimits
{
    public const int MaxAgentVersionLength = 50;
    public const int MaxHealthIssues = 20;
    public const int MaxHealthIssueLength = 256;
    public const int MaxActiveGameServers = 10_000;
}

/// <summary>Server methods the agent invokes. Implemented by Nodes' AgentHub.</summary>
/// <remarks>CommandResult and Telemetry are deferred to the commands campaign (D-S29).</remarks>
public interface IAgentHub
{
    /// <summary>Reports node health. The node is identified by the connection's certificate, never by the payload.</summary>
    Task<AgentHeartbeatAck> Heartbeat(AgentHeartbeat heartbeat);
}

/// <summary>Client methods Nodes invokes on the agent.</summary>
public interface IAgentHubClient
{
    /// <summary>A serialized CommandEnvelope. The shape and signing are owned by the commands campaign (#94).</summary>
    Task ReceiveCommand(string commandEnvelopeJson);

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
