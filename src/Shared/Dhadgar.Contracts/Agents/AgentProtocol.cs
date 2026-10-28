using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dhadgar.Contracts.Agents;

/// <summary>
/// Length and size limits applied to agent hub messages on both sides.
/// Unless stated otherwise, string limits are in UTF-16 characters and byte limits are in UTF-8 bytes.
/// The hub surface and its frame limit (<c>AgentHubProtocol.MaxReceiveMessageBytes</c>) live in <c>Contracts.Nodes</c>.
/// </summary>
public static class AgentMessageLimits
{
    /// <summary>Maximum length of <see cref="AgentCommandEnvelope.CommandType"/>.</summary>
    public const int MaxCommandTypeLength = 128;

    /// <summary>
    /// Maximum UTF-8 size in bytes of <see cref="AgentCommandEnvelope.PayloadJson"/>, of the unescaped string.
    /// The frame limit <c>Dhadgar.Contracts.Nodes.AgentHubProtocol.MaxReceiveMessageBytes</c> is enforced separately.
    /// </summary>
    public const int MaxPayloadJsonBytes = 64 * 1024;

    /// <summary>
    /// Maximum UTF-8 size in bytes of <see cref="AgentCommandResult.ResultJson"/>, of the unescaped string.
    /// The frame limit <c>Dhadgar.Contracts.Nodes.AgentHubProtocol.MaxReceiveMessageBytes</c> is enforced separately.
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

    /// <summary>Maximum number of metrics in one <see cref="AgentTelemetry"/>.</summary>
    public const int MaxTelemetryMetrics = 500;

    /// <summary>Maximum length of one metric name in <see cref="AgentTelemetry.Metrics"/>.</summary>
    public const int MaxMetricNameLength = 128;

    /// <summary>Maximum length of the opaque nonce in <see cref="PingPayload.Nonce"/>, <see cref="PingResult.Nonce"/> and <c>IAgentHubClient.ReceivePing</c>.</summary>
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
/// Lenient about unknown members (D-40), strict about everything else, and read-only: unknown properties
/// are skipped; a missing required property, a null in a non-nullable member, a duplicate property
/// or nesting deeper than 8 throws <see cref="JsonException"/>.
/// </summary>
/// <remarks>
/// PayloadJson is untrusted input on the agent. Its signature must be verified over the raw
/// PayloadJson string before it is parsed. Enum values parsed from it must be checked with
/// <c>Enum.IsDefined</c>, because numeric enum values are not range-checked by the serializer.
/// The options are read-only once created, so no service or agent code can change the parse
/// for the whole process. Skipping unknown members never bypasses the depth limit.
/// </remarks>
public static class AgentJson
{
    /// <summary>Read-only serializer options built from <see cref="JsonSerializerDefaults.Web"/> with lenient unknown-member handling.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip, // D-40 (supersedes D-21's Disallow)
            AllowDuplicateProperties = false,                          // D-40: one meaning per member
            MaxDepth = 8,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
