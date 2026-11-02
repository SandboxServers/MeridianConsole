namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// The enrolled identity of this agent. It holds no secrets: the client certificate and its
/// private key stay in the platform certificate store.
/// </summary>
/// <param name="NodeId">The node ID assigned by the control plane during enrollment.</param>
/// <param name="OrganizationId">The organization the node belongs to (tenant isolation).</param>
/// <param name="EnrolledAt">When the agent was enrolled.</param>
/// <param name="CommandSigningPublicKey">Base64 SubjectPublicKeyInfo of the control plane's command signing key, pinned at enrollment. Null for identities written before command signing (Sprint 2).</param>
/// <param name="CommandSigningKeyId">Key id of <paramref name="CommandSigningPublicKey"/>. Null when the key is null.</param>
public sealed record AgentIdentity(
    Guid NodeId,
    Guid OrganizationId,
    DateTimeOffset EnrolledAt,
    string? CommandSigningPublicKey = null,
    string? CommandSigningKeyId = null);
