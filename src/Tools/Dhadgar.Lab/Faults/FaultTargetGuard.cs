using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Faults;

/// <summary>
/// A target that passed every blast-radius check, ready for a fault kind to act on.
/// </summary>
/// <param name="TargetId">The target id, such as <c>c:gateway</c>.</param>
/// <param name="Kind">The target kind in lower case: <c>container</c>, <c>proxy</c>, <c>database</c>, <c>file</c> or <c>member</c>.</param>
/// <param name="Name">The registered name.</param>
/// <param name="ContainerId">The container the checks passed on (for a proxy, the Toxiproxy container).</param>
/// <param name="Service">The service the target serves, when known; null in L19.</param>
public sealed record ResolvedTarget(string TargetId, string Kind, string Name, string? ContainerId, string? Service);

/// <summary>
/// Resolves every mutating fault target through three checks (R-BLAST): the registry (id present, kind allows the fault,
/// stored container id matches), the live inspect (label, name, context), and the tripwire (name and mounts). Each check is
/// its own injectable collaborator, so a test can fail one while the other two pass. <c>m:host</c> is refused for every fault kind.
/// </summary>
public sealed class FaultTargetGuard(ILabTargetRegistry registry, IDockerInspector inspector, ITripwire tripwire)
{
    private const string HostTargetId = "m:host";
    private const string ToxiproxyTargetId = "c:toxiproxy";
    private const string LabLabel = "meridian-lab";
    private const string LabNamePrefix = "/meridian-lab-";
    private const string LabMemberLabel = "meridian-lab-member";
    private const string LabMemberNamePrefix = "meridian-lab-m-";

    private static readonly HashSet<string> KnownFaultKinds = new(StringComparer.Ordinal)
    {
        "toxic", "proxy_disable", "partition", "kill", "pause", "disk_fill", "corrupt_file", "corrupt_registry", "db", "reset",
    };

    private static readonly Dictionary<LabTargetKind, IReadOnlySet<string>> AllowedKinds =
        new Dictionary<LabTargetKind, IReadOnlySet<string>>
        {
            [LabTargetKind.Container] = new HashSet<string>(StringComparer.Ordinal) { "kill", "pause", "disk_fill", "corrupt_file", "corrupt_registry" },
            [LabTargetKind.Proxy] = new HashSet<string>(StringComparer.Ordinal) { "toxic", "proxy_disable", "partition", "reset" },
            [LabTargetKind.Database] = new HashSet<string>(StringComparer.Ordinal) { "db" },
            [LabTargetKind.File] = new HashSet<string>(StringComparer.Ordinal), // no fault kind targets a file id; corrupt_file goes through c:nodes (review N8)
            [LabTargetKind.Member] = new HashSet<string>(StringComparer.Ordinal) { "kill", "pause", "disk_fill", "corrupt_file", "corrupt_registry", "reset" },
        };

    /// <summary>
    /// Resolves one target for one fault kind. Every refusal is <c>[Lab.BlastRadius]</c>, or <c>[Lab.InvalidArgument]</c> for an unknown fault kind.
    /// </summary>
    /// <param name="targetId">The target id.</param>
    /// <param name="faultKind">One of the fault kinds: toxic, proxy_disable, partition, kill, pause, disk_fill, corrupt_file, corrupt_registry, db, reset.</param>
    /// <param name="ct">Cancels the live inspect.</param>
    public async Task<Result<ResolvedTarget>> ResolveAsync(string targetId, string faultKind, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(faultKind);

        if (string.Equals(targetId, HostTargetId, StringComparison.Ordinal))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] host is read-only");
        }

        if (!KnownFaultKinds.Contains(faultKind))
        {
            return Result<ResolvedTarget>.Failure("[Lab.InvalidArgument] unknown fault kind");
        }

        // Check 1: the registry.
        var target = registry.Find(targetId);
        if (target is null)
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] target is not registered");
        }

        if (!AllowedKinds.TryGetValue(target.Kind, out var allowed) || !allowed.Contains(faultKind))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] fault kind is not allowed on this target");
        }

        if (target.Kind == LabTargetKind.Member)
        {
            return await ResolveMemberAsync(target, ct).ConfigureAwait(false);
        }

        if (target.Kind == LabTargetKind.Database)
        {
            // A database has no container. The registry entry is the whole target; DbFault checks its name before any SQL.
            return Result<ResolvedTarget>.Success(new ResolvedTarget(target.TargetId, "database", target.Name, null, null));
        }

        var containerId = target.ContainerId;
        if (target.Kind == LabTargetKind.Proxy)
        {
            if (ProxyCatalog.Find(target.Name) is null)
            {
                return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] proxy is not in the C10 table");
            }

            containerId = registry.Find(ToxiproxyTargetId)?.ContainerId;
        }

        if (string.IsNullOrEmpty(containerId))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] target has no registered container");
        }

        // Check 2: the live inspect (id, label, name, context). Check 3: the tripwire (name and mounts).
        var inspected = await inspector.InspectAsync(containerId, ct).ConfigureAwait(false);
        if (inspected.IsFailure)
        {
            return Result<ResolvedTarget>.Failure(inspected.Error);
        }

        var inspection = inspected.Value;
        if (!IdMatches(containerId, inspection.Id))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] container id does not match the registry");
        }

        if (!inspection.Labels.TryGetValue(LabLabel, out var label) || !string.Equals(label, "1", StringComparison.Ordinal))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] container is not labelled as lab-owned");
        }

        if (!inspection.Name.StartsWith(LabNamePrefix, StringComparison.Ordinal))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] container name is not a lab name");
        }

        if (!string.Equals(inspection.Context, inspector.LabContext, StringComparison.Ordinal))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] container is not in the lab context");
        }

        var tripped = tripwire.Check(inspection.Name, inspection.Mounts);
        if (tripped.IsFailure)
        {
            return Result<ResolvedTarget>.Failure(tripped.Error);
        }

        return Result<ResolvedTarget>.Success(new ResolvedTarget(
            target.TargetId,
            target.Kind.ToString().ToLowerInvariant(),
            target.Name,
            containerId,
            null));
    }

    /// <summary>
    /// The fault kinds a target of this kind accepts. Shown by <c>fault_list_targets</c>; the guard checks the same table.
    /// </summary>
    /// <param name="kind">The target kind.</param>
    public static IReadOnlySet<string> AllowedFor(LabTargetKind kind) =>
        AllowedKinds.TryGetValue(kind, out IReadOnlySet<string>? allowed) ? allowed : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a pool member (<c>m:&lt;member id&gt;</c>). A member's container is recreated on reset, so the stored container
    /// id goes stale; the live container is found by its exact lab name and must carry both the lab label and the member label.
    /// </summary>
    private async Task<Result<ResolvedTarget>> ResolveMemberAsync(LabTarget target, CancellationToken ct)
    {
        string memberId = target.Name;
        if (!IsSafeMemberId(memberId))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] member id is not a lab member id");
        }

        string containerName = LabMemberNamePrefix + memberId;
        var inspected = await inspector.InspectAsync(containerName, ct).ConfigureAwait(false);
        if (inspected.IsFailure)
        {
            return Result<ResolvedTarget>.Failure(inspected.Error);
        }

        var inspection = inspected.Value;
        if (!string.Equals(inspection.Name, "/" + containerName, StringComparison.Ordinal))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] container name is not the member's lab name");
        }

        if (!inspection.Labels.TryGetValue(LabLabel, out var label) || !string.Equals(label, "1", StringComparison.Ordinal))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] container is not labelled as lab-owned");
        }

        if (!inspection.Labels.TryGetValue(LabMemberLabel, out var owner) || !string.Equals(owner, memberId, StringComparison.Ordinal))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] container is not labelled as this lab member");
        }

        if (!string.Equals(inspection.Context, inspector.LabContext, StringComparison.Ordinal))
        {
            return Result<ResolvedTarget>.Failure("[Lab.BlastRadius] container is not in the lab context");
        }

        var tripped = tripwire.Check(inspection.Name, inspection.Mounts);
        if (tripped.IsFailure)
        {
            return Result<ResolvedTarget>.Failure(tripped.Error);
        }

        return Result<ResolvedTarget>.Success(new ResolvedTarget(target.TargetId, "member", memberId, inspection.Id, null));
    }

    /// <summary>
    /// A member id is 1 to 32 lower-case letters, digits or hyphens (<c>lin-01</c>, <c>win-01</c>). Anything else is refused
    /// before a name is built from it.
    /// </summary>
    private static bool IsSafeMemberId(string memberId)
    {
        if (memberId.Length is < 1 or > 32)
        {
            return false;
        }

        foreach (char c in memberId)
        {
            bool ok = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IdMatches(string stored, string live) =>
        string.Equals(stored, live, StringComparison.Ordinal)
        || (stored.Length >= 12 && live.StartsWith(stored, StringComparison.Ordinal));
}
