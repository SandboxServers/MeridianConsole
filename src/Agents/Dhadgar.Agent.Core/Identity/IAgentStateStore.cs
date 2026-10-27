using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Persists the agent identity between restarts.
/// </summary>
public interface IAgentStateStore
{
    /// <summary>
    /// Loads the persisted identity.
    /// </summary>
    /// <returns>Success with the identity; Failure with [AgentState.Missing] when no state file exists (the shared Result&lt;T&gt; cannot carry a null success); Failure when the file exists but is unreadable or invalid.</returns>
    Task<Result<AgentIdentity?>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the identity, replacing any previous state.
    /// </summary>
    Task<Result> SaveAsync(AgentIdentity identity, CancellationToken cancellationToken = default);
}
