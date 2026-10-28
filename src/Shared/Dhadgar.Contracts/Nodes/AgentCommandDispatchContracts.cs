namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// Request to dispatch a command to a node's agent through the control plane.
/// </summary>
/// <param name="CommandType">One of <see cref="Agents.AgentCommandTypes"/>.</param>
/// <param name="PayloadJson">Untrusted caller input. Nodes parses it with <c>AgentJson.Options</c>, validates the typed record, and signs <c>JsonSerializer.Serialize(typed, AgentJson.Options)</c>, never this string (D-40).</param>
/// <param name="ServerId">Server the command targets, if any.</param>
/// <param name="CorrelationId">Optional caller correlation id.</param>
public sealed record DispatchAgentCommandRequest(string CommandType, string PayloadJson, Guid? ServerId, string? CorrelationId);

/// <summary>
/// Response returned once a command has been accepted for dispatch.
/// </summary>
/// <param name="CommandId">Id assigned to the command.</param>
/// <param name="IssuedAt">When the command was issued.</param>
/// <param name="ExpiresAt">When the command expires if not executed.</param>
public sealed record DispatchAgentCommandResponse(Guid CommandId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
