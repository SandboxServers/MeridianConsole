using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Tools.Faults;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The blast-radius rules at the tool layer: every mutating tool that takes a target implements <see cref="IFaultTool"/>, and
/// every fault tool refuses a target the guard must never let through.
/// </summary>
[Collection("LabHome")]
public sealed class BlastRadiusTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly LabCallContext Context = new("call-blast-radius", null, "test");
    private static readonly string[] TargetProperties = ["target", "proxy", "member", "database", "file"];

    [Fact]
    public async Task EveryLeasedToolWithATarget_ImplementsIFaultTool()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);

            List<string> missing = [];
            foreach (ILabTool tool in daemon.Services.GetServices<ILabTool>())
            {
                bool leased = ToolPolicy.GateFor(tool.Name) == LabGate.Leased;
                if (leased && HasTargetProperty(tool.InputSchema) && tool is not IFaultTool)
                {
                    missing.Add(tool.Name);
                }
            }

            Assert.Empty(missing);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("c:not-registered")]
    [InlineData("p:not-a-proxy")]
    [InlineData("d:postgres")]
    [InlineData("m:host")]
    public async Task EveryFaultTool_RefusesAnUnsafeTarget(string targetId)
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);

            IFaultTool[] tools = daemon.Services.GetServices<ILabTool>().OfType<IFaultTool>().ToArray();
            Assert.NotEmpty(tools);

            foreach (IFaultTool tool in tools)
            {
                var labTool = (ILabTool)tool;
                Result<JsonElement> result = await labTool.InvokeAsync(tool.SampleArgs(targetId), Context, CancellationToken.None);

                Assert.True(result.IsFailure, $"{labTool.Name} succeeded on {targetId}");
                Assert.True(
                    result.Error.StartsWith("[Lab.BlastRadius]", StringComparison.Ordinal)
                        || result.Error.StartsWith("[Lab.InvalidArgument]", StringComparison.Ordinal),
                    $"{labTool.Name} on {targetId} returned {result.Error}");
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Partition_OnAnEdgeWithNoProxy_IsInvalidArgument()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            ILabTool tool = daemon.Services.GetServices<ILabTool>().Single(t => t.Name == "fault_partition");

            Result<JsonElement> result = await tool.InvokeAsync(
                JsonSerializer.SerializeToElement(new { from = "nodes", to = "identity" }),
                Context,
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("[Lab.InvalidArgument] no proxy on that edge", result.Error);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CorruptFile_NodesNotRegistered_IsRefusedAndNoFileIsTouched()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            LabPaths paths = daemon.Services.GetRequiredService<LabPaths>();
            string caKey = Path.Combine(paths.State, "nodes-ca", "ca.key");
            Directory.CreateDirectory(Path.GetDirectoryName(caKey)!);
            byte[] original = "lab-test-ca-key"u8.ToArray();
            await File.WriteAllBytesAsync(caKey, original);
            ILabTool tool = daemon.Services.GetServices<ILabTool>().Single(t => t.Name == "fault_corrupt_file");

            Result<JsonElement> result = await tool.InvokeAsync(
                JsonSerializer.SerializeToElement(new { file = "nodes-ca-key", mode = "truncate" }),
                Context,
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("[Lab.BlastRadius] target is not registered", result.Error);
            Assert.Equal(original, await File.ReadAllBytesAsync(caKey));
            Assert.True(!Directory.Exists(paths.FaultBackups) || Directory.GetFileSystemEntries(paths.FaultBackups).Length == 0);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FaultsActive_OnAFreshDaemon_IsEmpty()
    {
        string root = NewRoot();
        try
        {
            await using LabDaemon daemon = await StartDaemonAsync(root);
            ILabTool tool = daemon.Services.GetServices<ILabTool>().Single(t => t.Name == "faults_active");

            Result<JsonElement> result = await tool.InvokeAsync(default, Context, CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(0, result.Value.GetProperty("faults").GetArrayLength());
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Member_LabelledAndNamedForItsOwnId_Resolves()
    {
        var guard = MemberGuard(LabMemberInspection("lin-01"));

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-01", "kill", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("member", result.Value.Kind);
        Assert.Equal("container-live", result.Value.ContainerId);
    }

    [Fact]
    public async Task Member_ContainerWithoutTheMemberLabel_IsRefused()
    {
        var inspection = LabMemberInspection("lin-01") with
        {
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["meridian-lab"] = "1" },
        };
        var guard = MemberGuard(inspection);

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-01", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] container is not labelled as this lab member", result.Error);
    }

    [Fact]
    public async Task Member_ContainerWithoutTheLabMarker_IsRefused()
    {
        var inspection = LabMemberInspection("lin-01") with { Labels = new Dictionary<string, string>(StringComparer.Ordinal) };
        var guard = MemberGuard(inspection);

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-01", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] container is not labelled as lab-owned", result.Error);
    }

    [Fact]
    public async Task Member_LabelOwnedByAnotherMember_IsRefused()
    {
        var inspection = LabMemberInspection("lin-01") with
        {
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["meridian-lab"] = "1", ["meridian-lab-member"] = "lin-02" },
        };
        var guard = MemberGuard(inspection);

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-01", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] container is not labelled as this lab member", result.Error);
    }

    [Fact]
    public async Task Member_LiveNameIsNotTheMembersLabName_IsRefused()
    {
        var inspection = LabMemberInspection("lin-01") with { Name = "/dhadgar-gateway" };
        var guard = MemberGuard(inspection);

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-01", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] container name is not the member's lab name", result.Error);
    }

    [Fact]
    public async Task Member_OutsideTheLabContext_IsRefused()
    {
        var inspection = LabMemberInspection("lin-01") with { Context = "production-ctx" };
        var guard = MemberGuard(inspection);

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-01", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] container is not in the lab context", result.Error);
    }

    [Fact]
    public async Task Member_TripwireRefusal_IsRefused()
    {
        var guard = MemberGuard(LabMemberInspection("lin-01"), new TripwireRefusing());

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-01", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] tripwire says no", result.Error);
    }

    [Theory]
    [InlineData("toxic")]
    [InlineData("db")]
    [InlineData("proxy_disable")]
    [InlineData("partition")]
    public async Task Member_FaultKindOutsideTheMemberSet_IsRefused(string faultKind)
    {
        var guard = MemberGuard(LabMemberInspection("lin-01"));

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-01", faultKind, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] fault kind is not allowed on this target", result.Error);
    }

    [Theory]
    [InlineData("lin-01/../../x")]
    [InlineData("Lin-01")]
    [InlineData("")]
    public async Task Member_UnsafeMemberId_IsRefusedBeforeAnyInspect(string memberId)
    {
        var inspector = new RecordingInspector(LabMemberInspection("lin-01"));
        var registry = new SingleTargetRegistry(new LabTarget("m:" + memberId, LabTargetKind.Member, memberId, "meridian-lab-m-x", DateTimeOffset.UnixEpoch));
        var guard = new FaultTargetGuard(registry, inspector, new TripwirePassing());

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:" + memberId, "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, inspector.Calls);
    }

    [Fact]
    public async Task Member_UnregisteredMemberId_IsRefused()
    {
        var guard = new FaultTargetGuard(new SingleTargetRegistry(null), new RecordingInspector(LabMemberInspection("lin-99")), new TripwirePassing());

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:lin-99", "kill", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] target is not registered", result.Error);
    }

    [Fact]
    public async Task MemberReset_Host_IsRefusedAsReadOnly()
    {
        var guard = MemberGuard(LabMemberInspection("lin-01"));

        Result<ResolvedTarget> result = await guard.ResolveAsync("m:host", "reset", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("[Lab.BlastRadius] host is read-only", result.Error);
    }

    private static FaultTargetGuard MemberGuard(ContainerInspection inspection, ITripwire? tripwire = null) =>
        new(
            new SingleTargetRegistry(new LabTarget("m:lin-01", LabTargetKind.Member, "lin-01", "meridian-lab-m-lin-01", DateTimeOffset.UnixEpoch)),
            new RecordingInspector(inspection),
            tripwire ?? new TripwirePassing());

    private static ContainerInspection LabMemberInspection(string memberId) =>
        new(
            "container-live",
            "/meridian-lab-m-" + memberId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["meridian-lab"] = "1", ["meridian-lab-member"] = memberId },
            [],
            0,
            DateTimeOffset.UnixEpoch,
            "lab-ctx");

    private sealed class SingleTargetRegistry(LabTarget? target) : ILabTargetRegistry
    {
        public IReadOnlyList<LabTarget> All => target is null ? [] : [target];

        public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId) =>
            Result<LabTarget>.Failure("[Lab.InvalidArgument] test registry is read-only");

        public LabTarget? Find(string targetId) => target is not null && string.Equals(target.TargetId, targetId, StringComparison.Ordinal) ? target : null;

        public void Remove(string targetId)
        {
        }

        public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class RecordingInspector(ContainerInspection inspection) : IDockerInspector
    {
        public int Calls { get; private set; }

        public string LabContext => "lab-ctx";

        public Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result<ContainerInspection>.Success(inspection));
        }
    }

    private sealed class TripwirePassing : ITripwire
    {
        public Result Check(string name, IReadOnlyList<MountInfo> mounts) => Result.Success();
    }

    private sealed class TripwireRefusing : ITripwire
    {
        public Result Check(string name, IReadOnlyList<MountInfo> mounts) => Result.Failure("[Lab.BlastRadius] tripwire says no");
    }

    private static bool HasTargetProperty(JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out JsonElement properties))
        {
            return false;
        }

        return TargetProperties.Any(name => properties.TryGetProperty(name, out _));
    }

    private static string NewRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "lab-l19-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task<LabDaemon> StartDaemonAsync(string root)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(LabDefaults.MinTokenBytes);
        string token = Base64Url.EncodeToString(bytes);
        Array.Clear(bytes);

        var started = await LabDaemon.StartAsync(new LabDaemonOptions(
            Port: 0,
            Token: token,
            DataRoot: root,
            EnforceSingleInstance: false,
            RefuseElevated: false,
            Time: new FakeTimeProvider(Start)), CancellationToken.None);

        Assert.True(started.IsSuccess, started.Error);
        return started.Value;
    }
}
