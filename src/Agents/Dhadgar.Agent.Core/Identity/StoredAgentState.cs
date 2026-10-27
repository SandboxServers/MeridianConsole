namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// What the state store holds. <see cref="Identity"/> is null when no state file exists (first run).
/// A non-null wrapper, because the shared Result&lt;T&gt; cannot carry a successful null (D-S232).
/// </summary>
public sealed record StoredAgentState(AgentIdentity? Identity)
{
    /// <summary>No state file exists: the agent has never been enrolled on this machine.</summary>
    public static StoredAgentState None { get; } = new(Identity: null);
}
