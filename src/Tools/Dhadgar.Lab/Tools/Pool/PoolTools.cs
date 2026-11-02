using System.Text.Json;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Tools.Faults;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tools.Pool;

/// <summary>
/// Arguments of <c>pool_acquire</c>. <c>constraints</c> is the C13 record; <c>purpose</c> says why, and is never a secret.
/// </summary>
public sealed record PoolAcquireArgs(PoolConstraints Constraints, string Purpose);

/// <summary>
/// Arguments of <c>pool_release</c>. <c>member</c> is a pool member id from <c>pool_acquire</c>; <c>m:&lt;id&gt;</c> is accepted too.
/// </summary>
public sealed record PoolReleaseArgs(string Member);

/// <summary>
/// Arguments of <c>member_reset</c>. <c>member</c> is a pool member id; <c>m:&lt;id&gt;</c> is accepted too.
/// </summary>
public sealed record MemberResetArgs(string Member);

/// <summary>
/// Result of <c>pool_acquire</c>: the members the lease now holds.
/// </summary>
public sealed record PoolAcquireResult(IReadOnlyList<PoolMember> Members);

/// <summary>
/// Result of <c>pool_release</c>.
/// </summary>
public sealed record PoolReleasedResult(bool Released);

/// <summary>
/// <c>pool_list</c> (Open): the providers' availability and the current members.
/// </summary>
public sealed class PoolListTool : LabTool<NoArguments>
{
    private readonly PoolManager _pool;

    /// <summary>
    /// Creates the tool over the pool manager.
    /// </summary>
    public PoolListTool(PoolManager pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <inheritdoc />
    public override string Name => "pool_list";

    /// <inheritdoc />
    public override string Description => "List the pool providers, whether each is available, and the current members.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(NoArguments args, LabCallContext context, CancellationToken ct)
    {
        PoolListing listing = await _pool.ListAsync(ct).ConfigureAwait(false);
        return Result<object>.Success(listing);
    }
}

/// <summary>
/// <c>pool_acquire</c> (Leased): provisions members that match the constraints for the lease.
/// </summary>
public sealed class PoolAcquireTool : LabTool<PoolAcquireArgs>
{
    private readonly PoolManager _pool;

    /// <summary>
    /// Creates the tool over the pool manager.
    /// </summary>
    public PoolAcquireTool(PoolManager pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <inheritdoc />
    public override string Name => "pool_acquire";

    /// <inheritdoc />
    public override string Description =>
        "Provision pool members that match the constraints (os, min_os_build, sku, isolation, kind, arch, reboot, count) for this lease.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(PoolAcquireArgs args, LabCallContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.LeaseId))
        {
            return Result<object>.Failure("[Lab.Lease] pool_acquire needs a lease");
        }

        Result<IReadOnlyList<PoolMember>> acquired = await _pool
            .AcquireAsync(args.Constraints, args.Purpose, context.LeaseId, ct)
            .ConfigureAwait(false);
        return acquired.IsFailure
            ? Result<object>.Failure(acquired.Error)
            : Result<object>.Success(new PoolAcquireResult(acquired.Value));
    }
}

/// <summary>
/// <c>pool_release</c> (Leased): destroys one container member. The host is refused.
/// </summary>
public sealed class PoolReleaseTool : LabTool<PoolReleaseArgs>
{
    private readonly PoolManager _pool;

    /// <summary>
    /// Creates the tool over the pool manager.
    /// </summary>
    public PoolReleaseTool(PoolManager pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <inheritdoc />
    public override string Name => "pool_release";

    /// <inheritdoc />
    public override string Description => "Destroy one pool member the lease holds. Only registered lab member containers are destroyed; host is refused.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(PoolReleaseArgs args, LabCallContext context, CancellationToken ct)
    {
        Result released = await _pool.ReleaseAsync(MemberIds.FromArgument(args.Member), ct).ConfigureAwait(false);
        return released.IsFailure
            ? Result<object>.Failure(released.Error)
            : Result<object>.Success(new PoolReleasedResult(true));
    }
}

/// <summary>
/// <c>member_reset</c> (Leased): recreates one member from its image with the same id. The host is refused.
/// </summary>
public sealed class MemberResetTool : LabTool<MemberResetArgs>, IFaultTool
{
    private readonly PoolManager _pool;
    private readonly FaultTargetGuard _guard;

    /// <summary>
    /// Creates the tool over the pool manager and the fault target guard.
    /// </summary>
    public MemberResetTool(PoolManager pool, FaultTargetGuard guard)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) => FaultToolSupport.ToArgs(new MemberResetArgs(targetId));

    /// <inheritdoc />
    public override string Name => "member_reset";

    /// <inheritdoc />
    public override string Description => "Recreate one pool member from its image with the same id. Host is refused: it is read-only.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(MemberResetArgs args, LabCallContext context, CancellationToken ct)
    {
        string memberId = MemberIds.FromArgument(args.Member);
        if (memberId.Contains(':', StringComparison.Ordinal))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] member must be a pool member id or m:<id>");
        }

        Result<ResolvedTarget> resolved = await _guard.ResolveAsync("m:" + memberId, "reset", ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result<object>.Failure(resolved.Error);
        }

        Result<PoolMember> reset = await _pool.ResetAsync(memberId, ct).ConfigureAwait(false);
        return reset.IsFailure
            ? Result<object>.Failure(reset.Error)
            : Result<object>.Success(reset.Value);
    }
}

/// <summary>
/// Reads a member argument: the bare member id, or the target form <c>m:&lt;id&gt;</c>.
/// </summary>
internal static class MemberIds
{
    private const string TargetPrefix = "m:";

    /// <summary>
    /// The member id without the <c>m:</c> prefix. Never null.
    /// </summary>
    public static string FromArgument(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.StartsWith(TargetPrefix, StringComparison.Ordinal) ? member[TargetPrefix.Length..] : member;
    }
}
