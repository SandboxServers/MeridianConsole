namespace Dhadgar.Contracts.Agents;

/// <summary>
/// Payload of a <see cref="AgentCommandTypes.Ping"/> command.
/// </summary>
/// <param name="Nonce">Opaque value echoed back in <see cref="PingResult"/>, at most <see cref="AgentMessageLimits.MaxNonceLength"/> characters.</param>
public sealed record PingPayload(string? Nonce = null);

/// <summary>
/// Result of a <see cref="AgentCommandTypes.Ping"/> command.
/// </summary>
/// <param name="AgentVersion">Agent build version, at most <see cref="AgentMessageLimits.MaxAgentVersionLength"/> characters.</param>
/// <param name="AgentTime">The agent's clock at the time of the reply.</param>
/// <param name="Nonce">Nonce echoed from <see cref="PingPayload"/>.</param>
public sealed record PingResult(string AgentVersion, DateTimeOffset AgentTime, string? Nonce);

/// <summary>
/// Payload of a <see cref="AgentCommandTypes.StartServer"/> command.
/// </summary>
/// <remarks>
/// The agent must validate every field before it acts on it, even though Nodes signed the payload:
/// <list type="bullet">
/// <item><description><see cref="ServerJarUrl"/> must be https, must have no userinfo, its host must be in the trusted-host allowlist,
/// every redirect must be revalidated against the same allowlist, and it must be at most <see cref="AgentMessageLimits.MaxServerJarUrlLength"/> characters.</description></item>
/// <item><description><see cref="MinecraftVersion"/> must match <see cref="AgentMessageLimits.MinecraftVersionPattern"/>. It must never be used as a path
/// component without <c>PathValidator</c>.</description></item>
/// <item><description><see cref="ServerJarSha1"/> must be exactly <see cref="AgentMessageLimits.ServerJarSha1HexLength"/> hex characters. The agent must verify it before executing the jar.</description></item>
/// <item><description><see cref="MemoryMb"/> must be within <see cref="AgentMessageLimits.MinServerMemoryMb"/> to <see cref="AgentMessageLimits.MaxServerMemoryMb"/>.</description></item>
/// <item><description><see cref="Port"/> must be within <see cref="AgentMessageLimits.MinServerPort"/> to <see cref="AgentMessageLimits.MaxServerPort"/>.</description></item>
/// </list>
/// </remarks>
/// <param name="ServerId">Server to start.</param>
/// <param name="MinecraftVersion">Minecraft version to install.</param>
/// <param name="ServerJarUrl">Download URL of the server jar; https, trusted-host allowlist, no userinfo.</param>
/// <param name="ServerJarSha1">Expected SHA-1 of the server jar, 40 lower- or upper-case hex characters.</param>
/// <param name="MemoryMb">Heap size in megabytes.</param>
/// <param name="Port">Listen port.</param>
/// <param name="EulaAccepted">Whether the EULA was accepted.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Wire contract: the URL is a string so both sides serialize it identically.")]
public sealed record StartServerPayload(
    Guid ServerId, string MinecraftVersion, string ServerJarUrl, string ServerJarSha1,
    int MemoryMb, int Port, bool EulaAccepted);

/// <summary>
/// Payload of a <see cref="AgentCommandTypes.StopServer"/> command.
/// </summary>
/// <param name="ServerId">Server to stop.</param>
/// <param name="GracefulTimeoutSeconds">Seconds to wait for a graceful stop before killing the process; must be within
/// <see cref="AgentMessageLimits.MinGracefulTimeoutSeconds"/> to <see cref="AgentMessageLimits.MaxGracefulTimeoutSeconds"/>.</param>
public sealed record StopServerPayload(Guid ServerId, int GracefulTimeoutSeconds);

/// <summary>
/// Payload of a <see cref="AgentCommandTypes.ServerStatus"/> command.
/// </summary>
/// <param name="ServerId">Server to query.</param>
public sealed record ServerStatusPayload(Guid ServerId);

/// <summary>
/// Result of a <see cref="AgentCommandTypes.ServerStatus"/> command.
/// </summary>
/// <param name="ServerId">Server id.</param>
/// <param name="State">Current state.</param>
/// <param name="OsPid">OS process id, when running.</param>
/// <param name="UptimeSeconds">Seconds since the process started.</param>
public sealed record ServerStatusResult(Guid ServerId, AgentServerState State, int? OsPid, long UptimeSeconds);
