using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Pool;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Pool;

public sealed class PoolManagerTests
{
    private static readonly PoolConstraints AnyLinux = new("linux", null, null, null, null, null, null);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static LeaseHistoryEntry Ended() =>
        new("test", "pool", PoolHarness.Start, PoolHarness.Start, LeaseEndReason.Released, null, null);

    [Fact]
    public async Task Acquire_MatchingConstraints_ProvisionsRegisteredMembers()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux]);

        Result<IReadOnlyList<PoolMember>> result = await pool.Manager.AcquireAsync(AnyLinux with { Count = 2 }, "test", "lease-1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["lin-01", "lin-02"], result.Value.Select(m => m.MemberId));
        Assert.All(result.Value, m => Assert.Equal("m:" + m.MemberId, m.TargetId));
        Assert.NotNull(pool.Registry.Find("m:lin-01"));
        Assert.NotNull(pool.Registry.Find("m:lin-02"));
    }

    [Fact]
    public async Task Acquire_ConstraintNoProviderMatches_IsUnavailable()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux]);

        Result<IReadOnlyList<PoolMember>> result = await pool.Manager.AcquireAsync(
            new PoolConstraints("windows", 26100, null, null, null, null, null), "test", "lease-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Unavailable]", result.Error, StringComparison.Ordinal);
        Assert.Empty(linux.Provisioned);
    }

    [Fact]
    public async Task Acquire_MinOsBuildAboveTheProvider_IsUnavailable()
    {
        var windows = new FakePoolProvider("windows-container", FakePoolProvider.WindowsContainer);
        using var pool = new PoolHarness([windows]);

        Result<IReadOnlyList<PoolMember>> result = await pool.Manager.AcquireAsync(
            new PoolConstraints("windows", 99999, null, null, null, null, null), "test", "lease-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(windows.Provisioned);
    }

    [Fact]
    public async Task Acquire_ProviderNotAvailable_ReportsItsReason()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer, available: false);
        using var pool = new PoolHarness([linux]);

        Result<IReadOnlyList<PoolMember>> result = await pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.Unavailable] fake provider is down", result.Error);
    }

    [Fact]
    public async Task Acquire_PastTheLinuxLimit_IsPoolExhausted()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux]);

        Result<IReadOnlyList<PoolMember>> full = await pool.Manager.AcquireAsync(AnyLinux with { Count = PoolManager.LinuxMemberLimit }, "test", "lease-1", CancellationToken.None);
        Result<IReadOnlyList<PoolMember>> over = await pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);

        Assert.True(full.IsSuccess, full.Error);
        Assert.True(over.IsFailure);
        Assert.StartsWith("[Lab.PoolExhausted]", over.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acquire_PastTheConfiguredWindowsLimit_IsPoolExhausted()
    {
        var windows = new FakePoolProvider("windows-container", FakePoolProvider.WindowsContainer);
        using var pool = new PoolHarness([windows], maxWindows: 1);
        var constraints = new PoolConstraints("windows", null, null, null, null, null, null);

        Result<IReadOnlyList<PoolMember>> first = await pool.Manager.AcquireAsync(constraints, "test", "lease-1", CancellationToken.None);
        Result<IReadOnlyList<PoolMember>> second = await pool.Manager.AcquireAsync(constraints, "test", "lease-1", CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsFailure);
        Assert.StartsWith("[Lab.PoolExhausted]", second.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task Acquire_CountOutOfRange_IsInvalidArgument(int count)
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux]);

        Result<IReadOnlyList<PoolMember>> result = await pool.Manager.AcquireAsync(AnyLinux with { Count = count }, "test", "lease-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeaseEnd_DestroysEveryContainerMember()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux]);
        _ = await pool.Manager.AcquireAsync(AnyLinux with { Count = 2 }, "test", "lease-1", CancellationToken.None);

        await pool.Manager.OnLeaseEndedAsync(Ended(), CancellationToken.None);

        Assert.Equal(["lin-01", "lin-02"], linux.Destroyed.Order());
        PoolListing listing = await pool.Manager.ListAsync(CancellationToken.None);
        Assert.Empty(listing.Members);
        Assert.Null(pool.Registry.Find("m:lin-01"));
    }

    [Fact]
    public async Task Release_DestroysOneMember_AndLeavesTheOther()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux]);
        _ = await pool.Manager.AcquireAsync(AnyLinux with { Count = 2 }, "test", "lease-1", CancellationToken.None);

        Result released = await pool.Manager.ReleaseAsync("lin-01", CancellationToken.None);

        Assert.True(released.IsSuccess, released.Error);
        Assert.Equal(["lin-01"], linux.Destroyed);
        PoolListing listing = await pool.Manager.ListAsync(CancellationToken.None);
        Assert.Equal(["lin-02"], listing.Members.Select(m => m.MemberId));
    }

    [Fact]
    public async Task Release_UnknownMember_IsNotFound()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux]);

        Result released = await pool.Manager.ReleaseAsync("lin-77", CancellationToken.None);

        Assert.True(released.IsFailure);
        Assert.StartsWith("[Lab.NotFound]", released.Error, StringComparison.Ordinal);
        Assert.Empty(linux.Destroyed);
    }

    [Fact]
    public async Task Reset_KeepsTheMemberId_AndReturnsItLeased()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux]);
        _ = await pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);

        Result<PoolMember> reset = await pool.Manager.ResetAsync("lin-01", CancellationToken.None);

        Assert.True(reset.IsSuccess, reset.Error);
        Assert.Equal("lin-01", reset.Value.MemberId);
        Assert.Equal(MemberState.Leased, reset.Value.State);
        Assert.Equal(["lin-01"], linux.Reset);
    }

    public static TheoryData<string, string, string> NotAMember => new()
    {
        { "/meridian-lab-m-lin-01", "", "unlabelled" },
        { "/meridian-lab-m-lin-01", "meridian-lab=1", "lab-only label, no member label" },
        { "/dhadgar-gateway", "meridian-lab=1;meridian-lab-member=lin-01", "other container name" },
    };

    [Theory]
    [MemberData(nameof(NotAMember))]
    public async Task Reset_AndRelease_RefuseAContainerThatIsNotTheLabMember(string name, string labelPairs, string why)
    {
        var labels = labelPairs.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : labelPairs.Split(';').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([linux], inspector: new FixedInspector(name, labels));
        _ = await pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);

        Result<PoolMember> reset = await pool.Manager.ResetAsync("lin-01", CancellationToken.None);
        Result released = await pool.Manager.ReleaseAsync("lin-01", CancellationToken.None);

        Assert.True(reset.IsFailure, why);
        Assert.StartsWith("[Lab.BlastRadius]", reset.Error, StringComparison.Ordinal);
        Assert.True(released.IsFailure, why);
        Assert.Empty(linux.Reset);
        Assert.Empty(linux.Destroyed);
    }

    [Fact]
    public async Task Start_DestroysWhatPoolJsonListsAndClearsIt()
    {
        var first = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([first]);
        _ = await pool.Manager.AcquireAsync(AnyLinux with { Count = 2 }, "test", "lease-1", CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(pool.Paths.State, "pool.json")));

        // A new daemon: a fresh manager and provider over the same state folder, as after a crash.
        var second = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var restarted = new PoolHarness([second], root: pool.Root);
        await restarted.Manager.StartAsync(CancellationToken.None);

        Assert.Equal(["lin-01", "lin-02"], second.Destroyed.Order());
        PoolListing listing = await restarted.Manager.ListAsync(CancellationToken.None);
        Assert.Empty(listing.Members);
        Assert.Empty(JsonSerializer.Deserialize<List<PoolMember>>(File.ReadAllText(Path.Combine(restarted.Paths.State, "pool.json")), JsonOptions)!);
    }

    [Fact]
    public async Task Acquire_EmptyConstraints_NeverHandsOutTheHost()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer, available: false);
        using var pool = new PoolHarness([linux, new HostProvider()]);

        Result<IReadOnlyList<PoolMember>> open = await pool.Manager.AcquireAsync(new PoolConstraints(null, null, null, null, null, null, null), "test", "lease-1", CancellationToken.None);
        Assert.True(open.IsFailure);
        Assert.StartsWith("[Lab.Unavailable]", open.Error, StringComparison.Ordinal);

        Result<IReadOnlyList<PoolMember>> host = await pool.Manager.AcquireAsync(new PoolConstraints(null, null, null, null, "host", null, null), "test", "lease-1", CancellationToken.None);
        Assert.True(host.IsSuccess, host.Error);
        Assert.Equal(["host"], host.Value.Select(m => m.MemberId));
    }

    [Fact]
    public async Task LeaseEnd_ForgetsTheHostMember()
    {
        using var pool = new PoolHarness([new HostProvider()]);
        Result<IReadOnlyList<PoolMember>> acquired = await pool.Manager.AcquireAsync(new PoolConstraints(null, null, null, null, "host", null, null), "test", "lease-1", CancellationToken.None);
        Assert.True(acquired.IsSuccess, acquired.Error);

        await pool.Manager.OnLeaseEndedAsync(Ended(), CancellationToken.None);

        PoolListing listing = await pool.Manager.ListAsync(CancellationToken.None);
        Assert.Empty(listing.Members);
    }

    [Fact]
    public async Task Reset_FailsAndTheContainerIsGone_ReleaseStillForgetsTheMember()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer) { FailReset = true };
        var inspector = new FlakyInspector();
        using var pool = new PoolHarness([linux], inspector: inspector);
        _ = await pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);

        Result<PoolMember> reset = await pool.Manager.ResetAsync("lin-01", CancellationToken.None);
        Assert.True(reset.IsFailure);
        inspector.Gone = true;

        Result released = await pool.Manager.ReleaseAsync("lin-01", CancellationToken.None);

        Assert.True(released.IsSuccess, released.Error);
        Assert.Empty(linux.Destroyed);
        PoolListing listing = await pool.Manager.ListAsync(CancellationToken.None);
        Assert.Empty(listing.Members);
    }

    [Fact]
    public async Task Acquire_FailedRunThenRetry_ReusesTheMemberId()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer) { FailProvision = true };
        using var pool = new PoolHarness([linux]);

        Result<IReadOnlyList<PoolMember>> failed = await pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);
        Assert.True(failed.IsFailure);
        PoolListing afterFailure = await pool.Manager.ListAsync(CancellationToken.None);
        Assert.Empty(afterFailure.Members);

        linux.FailProvision = false;
        Result<IReadOnlyList<PoolMember>> retried = await pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);

        Assert.True(retried.IsSuccess, retried.Error);
        Assert.Equal(["lin-01"], retried.Value.Select(m => m.MemberId));
    }

    [Fact]
    public async Task Acquire_CancelledMidProvision_LeavesNoMembers()
    {
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer) { CancelFrom = 2 };
        using var pool = new PoolHarness([linux]);

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pool.Manager.AcquireAsync(AnyLinux with { Count = 2 }, "test", "lease-1", CancellationToken.None));

        Assert.Equal(["lin-01"], linux.Destroyed);
        PoolListing listing = await pool.Manager.ListAsync(CancellationToken.None);
        Assert.Empty(listing.Members);
    }

    [Fact]
    public async Task Acquire_CancelledWhileSavingPending_ForgetsThePendingMember()
    {
        using var cancel = new CancellationTokenSource();
        var linux = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer) { CancelOnAvailable = cancel };
        using var pool = new PoolHarness([linux]);

        // File writes cancelled by the token surface as TaskCanceledException, a subclass of OperationCanceledException.
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", cancel.Token));

        Assert.Empty(linux.Provisioned);
        PoolListing listing = await pool.Manager.ListAsync(CancellationToken.None);
        Assert.Empty(listing.Members);
    }

    [Fact]
    public async Task Acquire_CrashMidProvision_StartDestroysTheListedMembers()
    {
        var first = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer) { CrashFrom = 2 };
        using var pool = new PoolHarness([first]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pool.Manager.AcquireAsync(AnyLinux with { Count = 2 }, "test", "lease-1", CancellationToken.None));

        // The second daemon finds the first member and the one that was being provisioned, both listed before their runs.
        var second = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var restarted = new PoolHarness([second], root: pool.Root);
        await restarted.Manager.StartAsync(CancellationToken.None);

        Assert.Equal(["lin-01", "lin-02"], second.Destroyed.Order());
    }

    [Fact]
    public async Task Start_ForgetsAProvisioningEntryWhoseContainerWasNeverCreated()
    {
        var first = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer) { CrashFrom = 1 };
        using var pool = new PoolHarness([first]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None));
        string file = Path.Combine(pool.Paths.State, "pool.json");
        Assert.Single(JsonSerializer.Deserialize<List<PoolMember>>(File.ReadAllText(file), JsonOptions)!);

        // The restarted daemon finds no container for the pending entry, so it is forgotten rather than kept as leftover.
        var second = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        var inspector = new FlakyInspector { Gone = true };
        using var restarted = new PoolHarness([second], root: pool.Root, inspector: inspector);
        await restarted.Manager.StartAsync(CancellationToken.None);

        Assert.Empty(JsonSerializer.Deserialize<List<PoolMember>>(File.ReadAllText(file), JsonOptions)!);
    }

    [Fact]
    public async Task Start_KeepsAnUnreadablePoolJsonAside()
    {
        using var pool = new PoolHarness([new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer)]);
        _ = Directory.CreateDirectory(pool.Paths.State);
        string file = Path.Combine(pool.Paths.State, "pool.json");
        File.WriteAllText(file, "{ not json");

        await pool.Manager.StartAsync(CancellationToken.None);

        Assert.Equal("{ not json", File.ReadAllText(file + ".bad"));
    }

    [Fact]
    public async Task Start_KeepsAListedEntryWhoseContainerNameDoesNotFollowFromItsId()
    {
        var first = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var pool = new PoolHarness([first]);
        _ = await pool.Manager.AcquireAsync(AnyLinux, "test", "lease-1", CancellationToken.None);
        string file = Path.Combine(pool.Paths.State, "pool.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("meridian-lab-m-lin-01", "meridian-lab-m-lin-02", StringComparison.Ordinal));

        var second = new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer);
        using var restarted = new PoolHarness([second], root: pool.Root);
        await restarted.Manager.StartAsync(CancellationToken.None);

        Assert.Empty(second.Destroyed);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Start_RemovesAStrayLabelledMemberContainer_ThroughTheGuard()
    {
        var docker = new FakeDockerCli { Listing = "meridian-lab-m-lin-09\nother-container\n" };
        using var pool = new PoolHarness([new FakePoolProvider("linux-container", FakePoolProvider.LinuxContainer)], docker: docker);

        await pool.Manager.StartAsync(CancellationToken.None);

        Assert.Single(docker.Calls, c => c[0] == "rm");
        Assert.Contains(docker.Calls, c => c.SequenceEqual(["rm", "-f", "meridian-lab-m-lin-09"]));
        Assert.Empty(pool.Registry.All);
    }
}

/// <summary>
/// Answers like a lab member until <see cref="Gone"/> is set, then answers the way a removed container does.
/// </summary>
internal sealed class FlakyInspector : IDockerInspector
{
    private readonly LabMemberInspector _inner = new();

    public bool Gone { get; set; }

    public string LabContext => "lab-ctx";

    public Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct) =>
        Gone
            ? Task.FromResult(Result<ContainerInspection>.Failure("[Lab.BlastRadius] container could not be inspected"))
            : _inner.InspectAsync(containerId, ct);
}
