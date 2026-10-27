namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// The enrolled identity of this agent. It holds no secrets: the client certificate and its
/// private key stay in the platform certificate store.
/// </summary>
/// <param name="NodeId">The node ID assigned by the control plane during enrollment.</param>
/// <param name="OrganizationId">The organization the node belongs to (tenant isolation).</param>
/// <param name="EnrolledAt">When the agent was enrolled.</param>
public sealed record AgentIdentity(Guid NodeId, Guid OrganizationId, DateTimeOffset EnrolledAt);
