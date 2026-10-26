namespace Dhadgar.Contracts.Agents;

/// <summary>
/// Wire-protocol constants shared by Nodes (hub host) and the agent (hub client).
/// </summary>
public static class AgentProtocol
{
    /// <summary>Current agent hub protocol version.</summary>
    public const int Version = 1;

    /// <summary>HTTP header carrying the protocol version on hub negotiate and connection.</summary>
    public const string VersionHeader = "X-Agent-Protocol";

    /// <summary>Hub path, identical on the public route and the Nodes service path.</summary>
    public const string HubPath = "/hubs/agent";

    /// <summary>Maximum SignalR message size; Nodes sets HubOptions.MaximumReceiveMessageSize to this.</summary>
    public const int MaxHubMessageBytes = 128 * 1024;
}

/// <summary>
/// Length and size limits applied to agent hub messages on both sides.
/// </summary>
public static class AgentMessageLimits
{
    /// <summary>Maximum length of <see cref="AgentCommandEnvelope.CommandType"/>.</summary>
    public const int MaxCommandTypeLength = 128;

    /// <summary>Maximum UTF-8 size in bytes of <see cref="AgentCommandEnvelope.PayloadJson"/>.</summary>
    public const int MaxPayloadJsonBytes = 64 * 1024;

    /// <summary>Maximum UTF-8 size in bytes of <see cref="AgentCommandResult.ResultJson"/>.</summary>
    public const int MaxResultJsonBytes = 64 * 1024;

    /// <summary>Maximum length of an error message.</summary>
    public const int MaxErrorMessageLength = 1024;

    /// <summary>Maximum length of a correlation id.</summary>
    public const int MaxCorrelationIdLength = 128;

    /// <summary>Maximum number of lines in one <see cref="AgentConsoleBatch"/>.</summary>
    public const int MaxConsoleLinesPerBatch = 200;

    /// <summary>Maximum characters per console line; longer lines are truncated by the sender.</summary>
    public const int MaxConsoleLineLength = 4096;

    /// <summary>Maximum sum of line text lengths, in characters, in one <see cref="AgentConsoleBatch"/>.</summary>
    public const int MaxConsoleBatchChars = 64 * 1024;

    /// <summary>Maximum number of warnings in one <see cref="AgentHeartbeat"/>.</summary>
    public const int MaxHeartbeatWarnings = 20;

    /// <summary>Maximum number of metrics in one <see cref="AgentTelemetry"/>.</summary>
    public const int MaxTelemetryMetrics = 500;
}

/// <summary>
/// Command type names understood by the agent. Matching is case-sensitive.
/// </summary>
public static class AgentCommandTypes
{
    /// <summary>Liveness round trip; payload <see cref="PingPayload"/>.</summary>
    public const string Ping = "Ping";

    /// <summary>Start a Minecraft server; payload <see cref="StartServerPayload"/>.</summary>
    public const string StartServer = "StartServer";

    /// <summary>Stop a server; payload <see cref="StopServerPayload"/>.</summary>
    public const string StopServer = "StopServer";

    /// <summary>Read a server's state; payload <see cref="ServerStatusPayload"/>.</summary>
    public const string ServerStatus = "ServerStatus";

    /// <summary>All known command types.</summary>
    public static IReadOnlyCollection<string> All { get; } = [Ping, StartServer, StopServer, ServerStatus];

    /// <summary>Returns true when <paramref name="commandType"/> is one of <see cref="All"/> (ordinal, case-sensitive).</summary>
    public static bool IsKnown(string? commandType) =>
        commandType is not null && All.Contains(commandType, StringComparer.Ordinal);
}

/// <summary>
/// JSON options for PayloadJson and ResultJson on both sides (camelCase, enums as numbers).
/// </summary>
public static class AgentJson
{
    /// <summary>Serializer options built from <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>.</summary>
    public static System.Text.Json.JsonSerializerOptions Options { get; } =
        new(System.Text.Json.JsonSerializerDefaults.Web);
}
