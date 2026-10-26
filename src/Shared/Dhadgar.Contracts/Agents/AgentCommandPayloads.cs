namespace Dhadgar.Contracts.Agents;

/// <summary>
/// Payload of a <see cref="AgentCommandTypes.Ping"/> command.
/// </summary>
/// <param name="Nonce">Opaque value echoed back in <see cref="PingResult"/>.</param>
public sealed record PingPayload(string? Nonce);

/// <summary>
/// Result of a <see cref="AgentCommandTypes.Ping"/> command.
/// </summary>
/// <param name="AgentVersion">Agent build version.</param>
/// <param name="AgentTime">The agent's clock at the time of the reply.</param>
/// <param name="Nonce">Nonce echoed from <see cref="PingPayload"/>.</param>
public sealed record PingResult(string AgentVersion, DateTimeOffset AgentTime, string? Nonce);

/// <summary>
/// Payload of a <see cref="AgentCommandTypes.StartServer"/> command.
/// </summary>
/// <param name="ServerId">Server to start.</param>
/// <param name="MinecraftVersion">Minecraft version to install.</param>
/// <param name="ServerJarUrl">Download URL of the server jar; must pass the trusted-host check.</param>
/// <param name="ServerJarSha1">Expected SHA-1 of the server jar.</param>
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
/// <param name="GracefulTimeoutSeconds">Seconds to wait for a graceful stop before killing the process.</param>
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
