using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Provides the agent identity to the runtime once it is known (after enrollment or from the state store).
/// </summary>
public interface IAgentIdentityProvider
{
    /// <summary>
    /// The current identity, or null while the agent is not enrolled.
    /// </summary>
    AgentIdentity? Current { get; }

    /// <summary>
    /// Completes when an identity is set.
    /// </summary>
    Task<AgentIdentity> WaitForIdentityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the identity. Setting the same NodeId and OrganizationId again succeeds (EnrolledAt is ignored);
    /// a different NodeId or OrganizationId fails with [AgentIdentity.Changed] (an identity change needs a restart).
    /// </summary>
    Result Set(AgentIdentity identity);
}
