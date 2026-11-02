using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Tools.Cli;
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
    private readonly IDockerCli _docker;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the tool over the pool manager, with the Docker client and paths that install the lab CLI (L37) into
    /// each Linux member it acquires.
    /// </summary>
    public PoolAcquireTool(PoolManager pool, IDockerCli docker, LabPaths paths, IPathValidator validator)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
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
        if (acquired.IsFailure)
        {
            return Result<object>.Failure(acquired.Error);
        }

        // L37: when the latest build has a CLI, each Linux member gets it at /opt/lab/cli/ before the lease uses it.
        foreach (PoolMember member in acquired.Value!)
        {
            Result installed = await CliMemberInstall.InstallIntoMemberAsync(_docker, _paths, _validator, member, ct).ConfigureAwait(false);
            if (installed.IsFailure)
            {
                await ReleaseAcquiredAsync(acquired.Value).ConfigureAwait(false);
                return Result<object>.Failure(installed.Error);
            }
        }

        return Result<object>.Success(new PoolAcquireResult(acquired.Value));
    }

    /// <summary>
    /// Releases every member a failed acquire took, so none stays leased. The caller returns the original install error.
    /// A release failure is ignored here; the lease-end hook releases whatever is still leased. <c>ReleaseAsync</c> refuses the host.
    /// </summary>
    private async Task ReleaseAcquiredAsync(IReadOnlyList<PoolMember> members)
    {
        foreach (PoolMember member in members)
        {
            // CancellationToken.None: a failed acquire must never leave members leased.
            _ = await _pool.ReleaseAsync(member.MemberId, CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// <c>pool_release</c> (Leased): destroys one container member. The host is refused.
/// </summary>
public sealed class PoolReleaseTool : LabTool<PoolReleaseArgs>, IFaultTool
{
    private readonly PoolManager _pool;
    private readonly FaultTargetGuard _guard;

    /// <summary>
    /// Creates the tool over the pool manager and the fault target guard.
    /// </summary>
    public PoolReleaseTool(PoolManager pool, FaultTargetGuard guard)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    /// <inheritdoc />
    public JsonElement SampleArgs(string targetId) => FaultToolSupport.ToArgs(new PoolReleaseArgs(targetId));

    /// <inheritdoc />
    public override string Name => "pool_release";

    /// <inheritdoc />
    public override string Description => "Destroy one pool member the lease holds. Only registered lab member containers are destroyed; host is refused.";

    /// <inheritdoc />
    protected override async Task<Result<object>> ExecuteAsync(PoolReleaseArgs args, LabCallContext context, CancellationToken ct)
    {
        string memberId = MemberIds.FromArgument(args.Member);
        if (memberId.Contains(':', StringComparison.Ordinal))
        {
            return Result<object>.Failure("[Lab.InvalidArgument] member must be a pool member id or m:<id>");
        }

        // Release destroys the container, so it is held to the same guard as a reset.
        Result<ResolvedTarget> resolved = await _guard.ResolveAsync("m:" + memberId, "reset", ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result<object>.Failure(resolved.Error);
        }

        Result released = await _pool.ReleaseAsync(memberId, ct).ConfigureAwait(false);
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
    private readonly IDockerCli _docker;
    private readonly LabPaths _paths;
    private readonly IPathValidator _validator;

    /// <summary>
    /// Creates the tool over the pool manager, the fault target guard, and the Docker client, paths and validator that
    /// reinstall the lab CLI (L37) into a reset Linux member, because a reset recreates the container from the bare image.
    /// </summary>
    public MemberResetTool(PoolManager pool, FaultTargetGuard guard, IDockerCli docker, LabPaths paths, IPathValidator validator)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
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
        if (reset.IsFailure)
        {
            return Result<object>.Failure(reset.Error);
        }

        // The reset recreated the container from the bare image, so the CLI and its config are installed again.
        Result installed = await CliMemberInstall.InstallIntoMemberAsync(_docker, _paths, _validator, reset.Value!, ct).ConfigureAwait(false);
        return installed.IsFailure
            ? Result<object>.Failure(installed.Error)
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
