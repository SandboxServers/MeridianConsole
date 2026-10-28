using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The three blast-radius checks, each tested with the other two passing (R-BLAST).
/// </summary>
public sealed class FaultTargetGuardTests
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string LabContext = "lab-ctx";

    [Fact]
    public async Task ResolveAsync_RegisteredLabelledAndNamed_Succeeds()
    {
        var guard = Guard(Registry(), Inspector(), new PassingTripwire());

        var result = await guard.ResolveAsync("c:gateway", "kill", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("container", result.Value.Kind);
        Assert.Equal(ContainerId, result.Value.ContainerId);
    }

    [Fact]
    public async Task ResolveAsync_UnregisteredId_IsRefused()
    {
        var guard = Guard(Registry(), Inspector(), new PassingTripwire());

        var result = await guard.ResolveAsync("c:not-registered", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_RegisteredButNotLabelled_IsRefusedByInspect_WhileTripwirePasses()
    {
        var inspection = LabInspection() with { Labels = new Dictionary<string, string>() };
        var guard = Guard(Registry(), Inspector(inspection), new PassingTripwire());

        var result = await guard.ResolveAsync("c:gateway", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not labelled", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_LabelledContainerNamedDhadgarGateway_IsRefusedByTripwireAlone()
    {
        var inspection = LabInspection();
        var guard = Guard(Registry(), Inspector(inspection), new RefusingTripwire());

        var result = await guard.ResolveAsync("c:gateway", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
        Assert.Contains("product", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_ContainerNameNotLabName_IsRefusedByNameCheck()
    {
        var inspection = LabInspection() with { Name = "/dhadgar-gateway" };
        var guard = Guard(Registry(), Inspector(inspection), new PassingTripwire());

        var result = await guard.ResolveAsync("c:gateway", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not a lab name", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_LiveIdDiffersFromRegistry_IsRefused()
    {
        var inspection = LabInspection() with { Id = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff" };
        var guard = Guard(Registry(), Inspector(inspection), new PassingTripwire());

        var result = await guard.ResolveAsync("c:gateway", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("does not match", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_OtherDockerContext_IsRefused()
    {
        var guard = Guard(Registry(), Inspector(LabInspection() with { Context = "other" }), new PassingTripwire());

        var result = await guard.ResolveAsync("c:gateway", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("lab context", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_NamedVolumeMount_IsRefusedByRealTripwire()
    {
        var inspection = LabInspection() with
        {
            Mounts = [new MountInfo("volume", "data", "/var/lib/data")],
        };
        var guard = Guard(Registry(), Inspector(inspection), new Tripwire());

        var result = await guard.ResolveAsync("c:gateway", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("named volume", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("toxic")]
    [InlineData("kill")]
    [InlineData("db")]
    [InlineData("reset")]
    public async Task ResolveAsync_HostMember_IsReadOnlyForEveryKind(string faultKind)
    {
        var guard = Guard(Registry(), Inspector(), new PassingTripwire());

        var result = await guard.ResolveAsync("m:host", faultKind, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] host is read-only", result.Error);
    }

    [Fact]
    public async Task ResolveAsync_FaultKindNotAllowedOnTargetKind_IsRefused()
    {
        var guard = Guard(Registry(), Inspector(), new PassingTripwire());

        var result = await guard.ResolveAsync("c:gateway", "toxic", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_UnknownFaultKind_IsInvalidArgument()
    {
        var guard = Guard(Registry(), Inspector(), new PassingTripwire());

        var result = await guard.ResolveAsync("c:gateway", "explode", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_ProxyInCatalogWithToxiproxyRegistered_Succeeds()
    {
        var registry = Registry();
        registry.Register("c:toxiproxy", LabTargetKind.Container, "toxiproxy", ContainerId);
        registry.Register("p:gateway-nodes", LabTargetKind.Proxy, "gateway-nodes", null);
        var guard = Guard(registry, Inspector(LabInspection() with { Name = "/meridian-lab-toxiproxy" }), new PassingTripwire());

        var result = await guard.ResolveAsync("p:gateway-nodes", "toxic", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("proxy", result.Value.Kind);
    }

    [Fact]
    public async Task ResolveAsync_ProxyNotInCatalog_IsRefused()
    {
        var registry = Registry();
        registry.Register("c:toxiproxy", LabTargetKind.Container, "toxiproxy", ContainerId);
        registry.Register("p:not-a-proxy", LabTargetKind.Proxy, "not-a-proxy", null);
        var guard = Guard(registry, Inspector(), new PassingTripwire());

        var result = await guard.ResolveAsync("p:not-a-proxy", "toxic", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_DatabaseTargetNotRegistered_IsRefused()
    {
        var guard = Guard(Registry(), Inspector(), new PassingTripwire());

        var result = await guard.ResolveAsync("d:postgres", "db", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
    }

    private static FakeRegistry Registry()
    {
        var registry = new FakeRegistry();
        registry.Register("c:gateway", LabTargetKind.Container, "gateway", ContainerId);
        return registry;
    }

    private static FaultTargetGuard Guard(ILabTargetRegistry registry, IDockerInspector inspector, ITripwire tripwire) =>
        new(registry, inspector, tripwire);

    private static FakeInspector Inspector(ContainerInspection? inspection = null) =>
        new(inspection ?? LabInspection(), LabContext);

    private static ContainerInspection LabInspection() =>
        new(
            ContainerId,
            "/meridian-lab-gateway",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["meridian-lab"] = "1" },
            [],
            0,
            DateTimeOffset.UnixEpoch,
            LabContext);

    private sealed class FakeRegistry : ILabTargetRegistry
    {
        private readonly Dictionary<string, LabTarget> _targets = new(StringComparer.Ordinal);

        public IReadOnlyList<LabTarget> All => _targets.Values.ToList();

        public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId)
        {
            var target = new LabTarget(targetId, kind, name, containerId, DateTimeOffset.UnixEpoch);
            _targets[targetId] = target;
            return Result<LabTarget>.Success(target);
        }

        public LabTarget? Find(string targetId) => _targets.GetValueOrDefault(targetId);

        public void Remove(string targetId) => _targets.Remove(targetId);

        public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class FakeInspector(ContainerInspection inspection, string labContext) : IDockerInspector
    {
        public string LabContext { get; } = labContext;

        public Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct) =>
            Task.FromResult(Result<ContainerInspection>.Success(inspection));
    }

    private sealed class PassingTripwire : ITripwire
    {
        public Result Check(string name, IReadOnlyList<MountInfo> mounts) => Result.Success();
    }

    private sealed class RefusingTripwire : ITripwire
    {
        public Result Check(string name, IReadOnlyList<MountInfo> mounts) =>
            Result.Failure("[Lab.BlastRadius] container is a product or data container");
    }
}

/// <summary>
/// The tripwire rules on their own.
/// </summary>
public sealed class TripwireTests
{
    [Theory]
    [InlineData("/postgres")]
    [InlineData("/cache")]
    [InlineData("/messaging")]
    [InlineData("/dhadgar-gateway")]
    [InlineData("/DhadgarGateway")]
    public void Check_ProtectedName_IsRefused(string name)
    {
        var result = new Tripwire().Check(name, []);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_NamedVolumeMount_IsRefused()
    {
        var result = new Tripwire().Check("/meridian-lab-nodes", [new MountInfo("volume", "pg", "/var/lib/postgresql")]);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Check_BindMountAndLabName_Passes()
    {
        var result = new Tripwire().Check("/meridian-lab-nodes", [new MountInfo("bind", null, "/app/config")]);

        Assert.True(result.IsSuccess);
    }
}
