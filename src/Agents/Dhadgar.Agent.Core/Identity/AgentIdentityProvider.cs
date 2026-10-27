using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Identity;

/// <summary>
/// In-process holder of the agent identity. Waiters are released on the first successful <see cref="Set"/>.
/// </summary>
public sealed class AgentIdentityProvider : IAgentIdentityProvider
{
    private readonly object _lock = new();
    private readonly TaskCompletionSource<AgentIdentity> _identityTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AgentIdentity? _current;

    /// <inheritdoc />
    public AgentIdentity? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public Task<AgentIdentity> WaitForIdentityAsync(CancellationToken cancellationToken)
    {
        return _identityTcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Result Set(AgentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_lock)
        {
            if (_current is not null)
            {
                return _current.NodeId == identity.NodeId
                    ? Result.Success()
                    : Result.Failure("[AgentIdentity.Changed] restart required");
            }

            if (identity.NodeId == Guid.Empty || identity.OrganizationId == Guid.Empty)
            {
                return Result.Failure("[AgentIdentity.Invalid] NodeId and OrganizationId must not be empty");
            }

            _current = identity;
            _identityTcs.TrySetResult(identity);
            return Result.Success();
        }
    }
}
