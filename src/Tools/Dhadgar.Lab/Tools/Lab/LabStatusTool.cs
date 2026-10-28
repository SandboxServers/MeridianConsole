using Dhadgar.Lab.Daemon;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// <c>lab_status</c> (Open): the daemon's <see cref="StatusDto"/>, the same body as <c>GET /status</c>.
/// </summary>
public sealed class LabStatusTool : LabTool<NoArguments>
{
    private readonly IEnumerable<ILabStatusSource> _sources;
    private readonly DaemonIdentity _identity;

    /// <summary>
    /// Creates the tool over every registered status source.
    /// </summary>
    public LabStatusTool(IEnumerable<ILabStatusSource> sources, DaemonIdentity identity)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    /// <inheritdoc />
    public override string Name => "lab_status";

    /// <inheritdoc />
    public override string Description => "Daemon status: version, pid, lease holder (never the lease id), stack state, processes, faults and running job.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(NoArguments args, LabCallContext context, CancellationToken ct)
    {
        StatusDto status = StatusEndpoint.Build(_sources, _identity.Pid, _identity.StartedAt);
        return Task.FromResult(Result<object>.Success(status));
    }
}
