namespace Dhadgar.Contracts.Agents;

/// <summary>
/// Published by Nodes when a command reaches a terminal status.
/// </summary>
/// <param name="CommandId">The completed command.</param>
/// <param name="NodeId">Node that executed it.</param>
/// <param name="OrganizationId">Owning organization.</param>
/// <param name="CommandType">Command type.</param>
/// <param name="ServerId">Server the command targeted, if any.</param>
/// <param name="Status">Terminal status.</param>
/// <param name="ResultJson">Result payload, if any.</param>
/// <param name="ErrorCode">Machine-readable error code, if any.</param>
/// <param name="ErrorMessage">Human-readable error, if any.</param>
/// <param name="CorrelationId">Correlation id, if any.</param>
/// <param name="CompletedAt">When the command completed.</param>
public sealed record AgentCommandCompleted(
    Guid CommandId, Guid NodeId, Guid OrganizationId, string CommandType, Guid? ServerId,
    AgentCommandStatus Status, string? ResultJson, string? ErrorCode, string? ErrorMessage,
    string? CorrelationId, DateTimeOffset CompletedAt);

/// <summary>
/// Published by Nodes when a server's state changes.
/// </summary>
/// <param name="NodeId">Node the server runs on.</param>
/// <param name="OrganizationId">Owning organization.</param>
/// <param name="ServerId">Server id.</param>
/// <param name="State">New state.</param>
/// <param name="ExitCode">Exit code, when the process exited.</param>
/// <param name="Message">Optional detail.</param>
/// <param name="OccurredAt">When the change occurred.</param>
public sealed record AgentServerStatusChanged(
    Guid NodeId, Guid OrganizationId, Guid ServerId, AgentServerState State,
    int? ExitCode, string? Message, DateTimeOffset OccurredAt);

/// <summary>
/// Published by Nodes when console output arrives for a server.
/// </summary>
/// <param name="ServerId">Server id.</param>
/// <param name="OrganizationId">Owning organization.</param>
/// <param name="NodeId">Node the server runs on.</param>
/// <param name="Lines">Console lines. Each line's text is untrusted: consumers must encode it for the output context and must not treat it as markup or as log-safe text (see <see cref="AgentConsoleLine.Text"/>).</param>
public sealed record ServerConsoleOutput(
    Guid ServerId, Guid OrganizationId, Guid NodeId, IReadOnlyList<AgentConsoleLine> Lines);
