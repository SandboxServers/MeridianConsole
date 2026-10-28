using Dhadgar.Lab.Leasing;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools;

/// <summary>
/// No arguments. An empty object is the only accepted input.
/// </summary>
public sealed record NoArguments;

/// <summary>
/// Arguments of <c>lab_lease_acquire</c>.
/// </summary>
public sealed record LeaseAcquireArgs(string Owner, string Purpose, int? TtlS = null, bool Force = false, string? Reason = null);

/// <summary>
/// Arguments of <c>lab_lease_renew</c>.
/// </summary>
public sealed record LeaseRenewArgs(string LeaseId, int? TtlS = null);

/// <summary>
/// Arguments of <c>lab_lease_release</c>.
/// </summary>
public sealed record LeaseReleaseArgs(string LeaseId);

/// <summary>
/// Result of <c>lab_lease_acquire</c>. The only place the lease id leaves the daemon.
/// </summary>
public sealed record LeaseAcquireResult(string LeaseId, string Owner, string Purpose, DateTimeOffset Since, DateTimeOffset ExpiresAt);

/// <summary>
/// Result of <c>lab_lease_release</c>.
/// </summary>
public sealed record LeaseReleasedResult(bool Released);

/// <summary>
/// Result of <c>lab_lease_status</c>. Neither the current lease nor the history carries a lease id.
/// </summary>
public sealed record LeaseStatusResult(LeaseInfo? Current, IReadOnlyList<LeaseHistoryEntry> History);

/// <summary>
/// <c>lab_lease_acquire</c> (Open): takes the lease, or takes it over with a reason when forced.
/// </summary>
public sealed class LeaseAcquireTool : LabTool<LeaseAcquireArgs>
{
    private readonly LeaseBook _leases;

    /// <summary>
    /// Creates the tool over the lease book.
    /// </summary>
    public LeaseAcquireTool(LeaseBook leases)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
    }

    /// <inheritdoc />
    public override string Name => "lab_lease_acquire";

    /// <inheritdoc />
    public override string Description => "Take the single lab lease for a purpose. Returns the lease id, which every Leased tool needs.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(LeaseAcquireArgs args, LabCallContext context, CancellationToken ct)
    {
        // A missing required string is null at runtime; the book would throw on it, so it is refused here.
        if (args.Owner is null || args.Purpose is null)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] owner and purpose are required"));
        }

        Result<LeaseGrant> grant = _leases.Acquire(args.Owner, args.Purpose, args.TtlS, args.Force, args.Reason);
        if (grant.IsFailure)
        {
            return Task.FromResult(Result<object>.Failure(grant.Error));
        }

        LeaseInfo info = grant.Value.Info;
        var result = new LeaseAcquireResult(grant.Value.LeaseId, info.Owner, info.Purpose, info.Since, info.ExpiresAt);
        return Task.FromResult(Result<object>.Success(result));
    }
}

/// <summary>
/// <c>lab_lease_renew</c> (Open): extends the lease when the id matches.
/// </summary>
public sealed class LeaseRenewTool : LabTool<LeaseRenewArgs>
{
    private readonly LeaseBook _leases;

    /// <summary>
    /// Creates the tool over the lease book.
    /// </summary>
    public LeaseRenewTool(LeaseBook leases)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
    }

    /// <inheritdoc />
    public override string Name => "lab_lease_renew";

    /// <inheritdoc />
    public override string Description => "Extend the lab lease. The lease id must match the holder's.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(LeaseRenewArgs args, LabCallContext context, CancellationToken ct)
    {
        if (args.LeaseId is null)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] lease_id is required"));
        }

        Result<LeaseInfo> renewed = _leases.Renew(args.LeaseId, args.TtlS);
        return Task.FromResult(renewed.IsFailure
            ? Result<object>.Failure(renewed.Error)
            : Result<object>.Success(renewed.Value));
    }
}

/// <summary>
/// <c>lab_lease_release</c> (Open): ends the lease when the id matches.
/// </summary>
public sealed class LeaseReleaseTool : LabTool<LeaseReleaseArgs>
{
    private readonly LeaseBook _leases;

    /// <summary>
    /// Creates the tool over the lease book.
    /// </summary>
    public LeaseReleaseTool(LeaseBook leases)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
    }

    /// <inheritdoc />
    public override string Name => "lab_lease_release";

    /// <inheritdoc />
    public override string Description => "Release the lab lease. The lease id must match the holder's.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(LeaseReleaseArgs args, LabCallContext context, CancellationToken ct)
    {
        if (args.LeaseId is null)
        {
            return Task.FromResult(Result<object>.Failure("[Lab.InvalidArgument] lease_id is required"));
        }

        Result released = _leases.Release(args.LeaseId);
        return Task.FromResult(released.IsFailure
            ? Result<object>.Failure(released.Error)
            : Result<object>.Success(new LeaseReleasedResult(true)));
    }
}

/// <summary>
/// <c>lab_lease_status</c> (Open): the current lease and the ended leases, without ids.
/// </summary>
public sealed class LeaseStatusTool : LabTool<NoArguments>
{
    private readonly LeaseBook _leases;

    /// <summary>
    /// Creates the tool over the lease book.
    /// </summary>
    public LeaseStatusTool(LeaseBook leases)
    {
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
    }

    /// <inheritdoc />
    public override string Name => "lab_lease_status";

    /// <inheritdoc />
    public override string Description => "The current lab lease (owner, purpose, expiry) and the ended leases, oldest first.";

    /// <inheritdoc />
    protected override Task<Result<object>> ExecuteAsync(NoArguments args, LabCallContext context, CancellationToken ct)
    {
        var result = new LeaseStatusResult(_leases.Current, _leases.History);
        return Task.FromResult(Result<object>.Success(result));
    }
}
