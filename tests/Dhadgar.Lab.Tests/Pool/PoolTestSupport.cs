using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Pool;

/// <summary>
/// A pool manager over fakes: an in-memory registry, an inspector that answers like a healthy lab member, and fake providers.
/// </summary>
internal sealed class PoolHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    public PoolHarness(IEnumerable<IPoolProvider> providers, string? root = null, int? maxWindows = null, IDockerInspector? inspector = null)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), "lab-l30-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);
        Registry = new MemoryRegistry();
        Paths = LabPaths.FromRoot(Root);
        var settings = new Dictionary<string, string?>();
        if (maxWindows is int max)
        {
            settings["Lab:Pool:MaxWindows"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var guard = new FaultTargetGuard(Registry, inspector ?? new LabMemberInspector(), new PassingTripwire());
        Manager = new PoolManager(Paths, providers, Registry, guard, config, new FakeTimeProvider(Start), NullLogger<PoolManager>.Instance);
    }

    public string Root { get; }

    public LabPaths Paths { get; }

    public MemoryRegistry Registry { get; }

    public PoolManager Manager { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal sealed class MemoryRegistry : ILabTargetRegistry
{
    private readonly Dictionary<string, LabTarget> _targets = new(StringComparer.Ordinal);

    public IReadOnlyList<LabTarget> All => _targets.Values.ToArray();

    public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId)
    {
        var target = new LabTarget(targetId, kind, name, containerId, PoolHarness.Start);
        _targets[targetId] = target;
        return Result<LabTarget>.Success(target);
    }

    public LabTarget? Find(string targetId) => _targets.TryGetValue(targetId, out LabTarget? target) ? target : null;

    public void Remove(string targetId) => _targets.Remove(targetId);

    public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);
}

/// <summary>
/// Answers every inspect as the lab member container the registry names: lab label, member label for the id in the name, lab context.
/// </summary>
internal sealed class LabMemberInspector : IDockerInspector
{
    private const string NamePrefix = "meridian-lab-m-";

    public string LabContext => "lab-ctx";

    public Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct)
    {
        string memberId = containerId.StartsWith(NamePrefix, StringComparison.Ordinal) ? containerId[NamePrefix.Length..] : containerId;
        var inspection = new ContainerInspection(
            "live-" + memberId,
            "/" + NamePrefix + memberId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["meridian-lab"] = "1", ["meridian-lab-member"] = memberId },
            [],
            0,
            DateTimeOffset.UnixEpoch,
            "lab-ctx");
        return Task.FromResult(Result<ContainerInspection>.Success(inspection));
    }
}

/// <summary>
/// Answers every inspect with a container that is not what the pool registered: the given labels, name and context.
/// </summary>
internal sealed class FixedInspector(string name, IReadOnlyDictionary<string, string> labels, string context = "lab-ctx") : IDockerInspector
{
    public string LabContext => "lab-ctx";

    public Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct) =>
        Task.FromResult(Result<ContainerInspection>.Success(new ContainerInspection("live", name, labels, [], 0, DateTimeOffset.UnixEpoch, context)));
}

internal sealed class PassingTripwire : ITripwire
{
    public Result Check(string name, IReadOnlyList<MountInfo> mounts) => Result.Success();
}

/// <summary>
/// A provider that records what the manager asks of it.
/// </summary>
internal sealed class FakePoolProvider : IPoolProvider
{
    public FakePoolProvider(string kind, PoolDimensions dimensions, bool available = true)
    {
        Kind = kind;
        Dimensions = dimensions;
        Available = available;
    }

    public static PoolDimensions LinuxContainer { get; } = new("linux", null, "debian", "process", "container", "x64", "host", false);

    public static PoolDimensions WindowsContainer { get; } = new("windows", 26100, "servercore", "process", "container", "x64", "host", false);

    public string Kind { get; }

    public PoolDimensions Dimensions { get; }

    public bool Available { get; set; }

    public List<string> Provisioned { get; } = [];

    public List<string> Reset { get; } = [];

    public List<string> Destroyed { get; } = [];

    public Task<Result<bool>> IsAvailableAsync(CancellationToken ct) =>
        Task.FromResult(Available ? Result<bool>.Success(true) : Result<bool>.Failure("[Lab.Unavailable] fake provider is down"));

    public Task<Result<PoolMember>> ProvisionAsync(PoolConstraints constraints, string memberId, CancellationToken ct)
    {
        Provisioned.Add(memberId);
        return Task.FromResult(Result<PoolMember>.Success(new PoolMember(memberId, "m:" + memberId, "meridian-lab-m-" + memberId, Dimensions, MemberState.Ready, PoolHarness.Start)));
    }

    public Task<Result<PoolMember>> ResetAsync(PoolMember member, CancellationToken ct)
    {
        Reset.Add(member.MemberId);
        return Task.FromResult(Result<PoolMember>.Success(member));
    }

    public Task<Result> DestroyAsync(PoolMember member, CancellationToken ct)
    {
        Destroyed.Add(member.MemberId);
        return Task.FromResult(Result.Success());
    }
}
