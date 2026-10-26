using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Maximum size in bytes of one SignalR JSON hub frame, after JSON escaping.
    /// Nodes sets HubOptions.MaximumReceiveMessageSize to this. The content limits in
    /// <see cref="AgentMessageLimits"/> are UTF-8 bytes or characters of the unescaped content;
    /// a control character or non-ASCII character can grow to 6 bytes once escaped as \uXXXX,
    /// so this value is sized for the worst case of the largest allowed message.
    /// </summary>
    public const int MaxHubMessageBytes = 512 * 1024;
}

/// <summary>
/// Length and size limits applied to agent hub messages on both sides.
/// Unless stated otherwise, string limits are in UTF-16 characters and byte limits are in UTF-8 bytes.
/// </summary>
public static class AgentMessageLimits
{
    /// <summary>Maximum length of <see cref="AgentCommandEnvelope.CommandType"/>.</summary>
    public const int MaxCommandTypeLength = 128;

    /// <summary>
    /// Maximum UTF-8 size in bytes of <see cref="AgentCommandEnvelope.PayloadJson"/>, of the unescaped string.
    /// The frame limit <see cref="AgentProtocol.MaxHubMessageBytes"/> is enforced separately.
    /// </summary>
    public const int MaxPayloadJsonBytes = 64 * 1024;

    /// <summary>
    /// Maximum UTF-8 size in bytes of <see cref="AgentCommandResult.ResultJson"/>, of the unescaped string.
    /// The frame limit <see cref="AgentProtocol.MaxHubMessageBytes"/> is enforced separately.
    /// </summary>
    public const int MaxResultJsonBytes = 64 * 1024;

    /// <summary>Maximum length of an error message, also the bound for <see cref="AgentServerStatus.Message"/>.</summary>
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

    /// <summary>Maximum length of one warning in <see cref="AgentHeartbeat.Warnings"/>.</summary>
    public const int MaxWarningLength = 256;

    /// <summary>Maximum length of <see cref="AgentHeartbeat.AgentVersion"/> and <see cref="PingResult.AgentVersion"/>.</summary>
    public const int MaxAgentVersionLength = 64;

    /// <summary>Maximum number of server entries in one <see cref="AgentHeartbeat"/>.</summary>
    public const int MaxServersPerHeartbeat = 64;

    /// <summary>Maximum number of metrics in one <see cref="AgentTelemetry"/>.</summary>
    public const int MaxTelemetryMetrics = 500;

    /// <summary>Maximum length of one metric name in <see cref="AgentTelemetry.Metrics"/>.</summary>
    public const int MaxMetricNameLength = 128;

    /// <summary>Maximum length of the opaque nonce in <see cref="PingPayload.Nonce"/>, <see cref="PingResult.Nonce"/> and <see cref="IAgentHubClient.ReceivePing"/>.</summary>
    public const int MaxNonceLength = 64;

    /// <summary>
    /// Maximum lifetime of a command: <see cref="AgentCommandEnvelope.ExpiresAt"/> minus
    /// <see cref="AgentCommandEnvelope.IssuedAt"/> must not exceed this value, in seconds.
    /// The agent must reject a command with a longer lifetime.
    /// </summary>
    public const int MaxCommandLifetimeSeconds = 60;

    /// <summary>
    /// Allowed clock skew, in seconds. The agent must reject a command whose
    /// <see cref="AgentCommandEnvelope.IssuedAt"/> is later than now plus this value.
    /// </summary>
    public const int MaxClockSkewSeconds = 30;

    /// <summary>Maximum length of <see cref="StartServerPayload.MinecraftVersion"/>.</summary>
    public const int MaxMinecraftVersionLength = 32;

    /// <summary>
    /// Pattern that <see cref="StartServerPayload.MinecraftVersion"/> must match. The version is never
    /// used as a path component without <c>PathValidator</c>.
    /// </summary>
    public const string MinecraftVersionPattern = "^[0-9A-Za-z._-]{1,32}$";

    /// <summary>Maximum length of <see cref="StartServerPayload.ServerJarUrl"/>.</summary>
    public const int MaxServerJarUrlLength = 2048;

    /// <summary>Length of <see cref="StartServerPayload.ServerJarSha1"/> in hex characters.</summary>
    public const int ServerJarSha1HexLength = 40;

    /// <summary>Minimum <see cref="StartServerPayload.MemoryMb"/>.</summary>
    public const int MinServerMemoryMb = 512;

    /// <summary>Maximum <see cref="StartServerPayload.MemoryMb"/>.</summary>
    public const int MaxServerMemoryMb = 32 * 1024;

    /// <summary>Lowest allowed <see cref="StartServerPayload.Port"/>; ports below this are privileged or reserved.</summary>
    public const int MinServerPort = 1024;

    /// <summary>Highest allowed <see cref="StartServerPayload.Port"/>.</summary>
    public const int MaxServerPort = 65535;

    /// <summary>Minimum <see cref="StopServerPayload.GracefulTimeoutSeconds"/>.</summary>
    public const int MinGracefulTimeoutSeconds = 1;

    /// <summary>Maximum <see cref="StopServerPayload.GracefulTimeoutSeconds"/>.</summary>
    public const int MaxGracefulTimeoutSeconds = 300;
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
/// PayloadJson is untrusted input on the agent. Its signature must be verified over the raw
/// PayloadJson string before it is parsed. Enum values parsed from it must be checked with
/// <c>Enum.IsDefined</c>, because numeric enum values are not range-checked by the serializer.
/// </summary>
/// <remarks>
/// The options are read-only once created, so no service or agent code can change the parse
/// for the whole process. Missing required properties and unknown properties both throw
/// <see cref="JsonException"/>, nullable reference annotations are enforced, and depth is capped at 8.
/// </remarks>
public static class AgentJson
{
    /// <summary>Read-only serializer options built from <see cref="JsonSerializerDefaults.Web"/> with strict parsing.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 8,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
