using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// Persists the agent identity between restarts.
/// </summary>
public interface IAgentStateStore
{
    /// <summary>
    /// Loads the persisted state.
    /// </summary>
    /// <returns>
    /// Success(<see cref="StoredAgentState.None"/>) only when the state file does not exist;
    /// Success with an identity when the file is valid and trusted;
    /// Failure ([AgentState.Corrupt], [AgentState.Io], [AgentState.Config] or [AgentState.Untrusted]) otherwise.
    /// Callers enroll only on None and never on a Failure (D-S232).
    /// </returns>
    Task<Result<StoredAgentState>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the identity, replacing any previous state.
    /// </summary>
    Task<Result> SaveAsync(AgentIdentity identity, CancellationToken cancellationToken = default);
}
