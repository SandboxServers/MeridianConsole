using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The <c>kill</c> fault kind with fake docker, registry, inspector and journal (R-ARGS, R-BLAST, R-RESULT).
/// The shared fakes are also used by <see cref="PauseFaultTests"/>.
/// </summary>
public sealed class KillFaultTests
{
    internal const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    internal const string LabContext = "lab-ctx";

    [Fact]
    public async Task PrepareAsync_RecordsRestartCountAndStartTime()
    {
        var docker = new FakeDocker(_ => Ok("3|2026-10-01T10:00:00Z"));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.PrepareAsync(Target(), Args(), "f1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var heal = Heal(result.Value);
        Assert.Equal(ContainerId, heal.ContainerId);
        Assert.Equal("KILL", heal.Signal);
        Assert.Equal(3, heal.RestartCount);
        Assert.Equal("2026-10-01T10:00:00Z", heal.StartedAt);
        Assert.Equal(new[] { "inspect", "--format", "{{.RestartCount}}|{{.State.StartedAt}}", ContainerId }, docker.Calls[0]);
    }

    [Fact]
    public async Task PrepareAsync_SignalOutsideKillAndTerm_IsRefusedBeforeDocker()
    {
        var docker = new FakeDocker(_ => Ok("0|x"));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.PrepareAsync(Target(), Json(new KillArgs("HUP")), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
        Assert.Empty(docker.Calls);
    }

    [Fact]
    public async Task PrepareAsync_UnreadableInspectOutput_IsRefused()
    {
        var docker = new FakeDocker(_ => Ok("not-a-number"));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.PrepareAsync(Target(), Args(), "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Docker]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InjectAsync_RunsDockerKillWithSignalAsSeparateArguments()
    {
        var docker = new FakeDocker(_ => Ok(""));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());
        var heal = Json(new KillHealData(ContainerId, "TERM", 0, "2026-10-01T10:00:00Z"));

        var result = await fault.InjectAsync(Target(), Args(), heal, "f1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "kill", "--signal", "TERM", ContainerId }, docker.Calls.Single());
    }

    [Fact]
    public async Task InjectAsync_DockerRefusesKill_IsFailure()
    {
        var docker = new FakeDocker(_ => new DockerOutput(1, "", "no such container"));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());
        var heal = Json(new KillHealData(ContainerId, "KILL", 0, "2026-10-01T10:00:00Z"));

        var result = await fault.InjectAsync(Target(), Args(), heal, "f1", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task HealAsync_StartsTheContainer()
    {
        var docker = HealDocker(_ => Ok(ContainerId));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "start", ContainerId }, docker.Calls.Last());
        Assert.Equal(2, docker.Calls.Count);
    }

    [Fact]
    public async Task HealAsync_AlreadyRunning_IsSuccess()
    {
        var docker = HealDocker(_ => new DockerOutput(1, "", "Error: container is already running"));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task HealAsync_LabelInspectNoSuchObject_IsSuccessAndInspectsContainersOnly()
    {
        var docker = new FakeDocker(args => args[0] == "inspect"
            ? new DockerOutput(1, "", "Error: No such object: " + ContainerId)
            : Ok(ContainerId));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("--type", docker.Calls[0][1]);
        Assert.Equal("container", docker.Calls[0][2]);
    }

    [Fact]
    public async Task HealAsync_StartFailsForAnotherReason_IsFailure()
    {
        var docker = HealDocker(_ => new DockerOutput(1, "", "Error response from daemon: driver failed programming external connectivity"));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.Docker]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealAsync_InvalidJournalRef_MakesNoDockerCall()
    {
        var docker = new FakeDocker(_ => Ok(ContainerId));
        var entry = new FaultJournalEntry("f1", "kill", "c:gateway", Args(), Json(new KillHealData("--help", "KILL", 0, "2026-10-01T10:00:00Z")),
            FaultState.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), null);
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.HealAsync(entry, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.BlastRadius]", result.Error, StringComparison.Ordinal);
        Assert.Empty(docker.Calls);
    }

    [Fact]
    public async Task HealAsync_UnlabelledContainer_IsRefusedBeforeStart()
    {
        var docker = new FakeDocker(args => args[0] == "inspect" ? Ok("<no value>") : Ok(ContainerId));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not labelled", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(docker.Calls, c => c[0] == "start");
    }

    [Fact]
    public async Task HealAsync_ContainerGone_IsSuccess()
    {
        var docker = new FakeDocker(args => args[0] == "inspect"
            ? new DockerOutput(1, "", "Error: No such object: x")
            : new DockerOutput(1, "", "Error: No such container: x"));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.HealAsync(Entry(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task ReconcileAsync_UnownedKilledLabContainer_IsStarted()
    {
        var docker = new FakeDocker(args => args[0] == "inspect" ? Ok("exited|137") : Ok(ContainerId));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value);
        Assert.Contains(docker.Calls, c => c.SequenceEqual(new[] { "start", ContainerId }));
    }

    [Fact]
    public async Task ReconcileAsync_TargetOwnedByJournal_IsLeftAlone()
    {
        var docker = new FakeDocker(args => args[0] == "inspect" ? Ok("exited|137") : Ok(ContainerId));
        var journal = new FakeJournal(Entry());
        var fault = Fault(docker, Registry(), Inspector(), journal);

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value);
        Assert.DoesNotContain(docker.Calls, c => c[0] == "start");
    }

    [Fact]
    public async Task ReconcileAsync_CleanExit_IsLeftAlone()
    {
        var docker = new FakeDocker(args => args[0] == "inspect" ? Ok("exited|0") : Ok(ContainerId));
        var fault = Fault(docker, Registry(), Inspector(), new FakeJournal());

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value);
        Assert.DoesNotContain(docker.Calls, c => c[0] == "start");
    }

    [Fact]
    public async Task ReconcileAsync_NonLabContainer_IsNeverTouched()
    {
        var docker = new FakeDocker(_ => Ok("exited|137"));
        var inspection = LabInspection() with { Labels = new Dictionary<string, string>() };
        var fault = Fault(docker, Registry(), Inspector(inspection), new FakeJournal());

        var result = await fault.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value);
        Assert.Empty(docker.Calls);
    }

    private static KillFault Fault(FakeDocker docker, FakeRegistry registry, FakeInspector inspector, IFaultJournal journal) =>
        new(docker, registry, new FaultTargetGuard(registry, inspector, new PassingTripwire()), journal,
            NullLogger<KillFault>.Instance);

    internal static FakeRegistry Registry()
    {
        var registry = new FakeRegistry();
        registry.Register("c:gateway", LabTargetKind.Container, "gateway", ContainerId);
        return registry;
    }

    internal static FakeInspector Inspector(ContainerInspection? inspection = null) =>
        new(inspection ?? LabInspection(), LabContext);

    internal static ContainerInspection LabInspection() =>
        new(
            ContainerId,
            "/meridian-lab-gateway",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["meridian-lab"] = "1" },
            [],
            0,
            DateTimeOffset.UnixEpoch,
            LabContext);

    internal static ResolvedTarget Target() => new("c:gateway", "container", "gateway", ContainerId, "gateway");

    internal static FaultJournalEntry Entry() =>
        new("f1", "kill", "c:gateway", Args(), Json(new KillHealData(ContainerId, "KILL", 0, "2026-10-01T10:00:00Z")),
            FaultState.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), null);

    internal static JsonElement Args() => Json(new KillArgs());

    internal static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, LabJson.Options);

    internal static KillHealData Heal(JsonElement element) =>
        JsonSerializer.Deserialize<KillHealData>(element.GetRawText(), LabJson.Options)!;

    internal static DockerOutput Ok(string stdout) => new(0, stdout, "");

    /// <summary>A fake whose label inspect (security N4) answers "1"; every other call goes to <paramref name="respond"/>.</summary>
    internal static FakeDocker HealDocker(Func<IReadOnlyList<string>, DockerOutput> respond) =>
        new(args => args[0] == "inspect" && args.Count == 6 && args[4].Contains(".Config.Labels", StringComparison.Ordinal) ? Ok("1") : respond(args));

    internal sealed class FakeDocker(Func<IReadOnlyList<string>, DockerOutput> respond) : IDockerCli
    {
        public List<string[]> Calls { get; } = [];

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            return Task.FromResult(Result<DockerOutput>.Success(respond(args)));
        }
    }

    internal sealed class FakeRegistry : ILabTargetRegistry
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

    internal sealed class FakeInspector(ContainerInspection inspection, string labContext) : IDockerInspector
    {
        public string LabContext { get; } = labContext;

        public Task<Result<ContainerInspection>> InspectAsync(string containerId, CancellationToken ct) =>
            Task.FromResult(Result<ContainerInspection>.Success(inspection));
    }

    internal sealed class PassingTripwire : ITripwire
    {
        public Result Check(string name, IReadOnlyList<MountInfo> mounts) => Result.Success();
    }

    internal sealed class FakeJournal(params FaultJournalEntry[] entries) : IFaultJournal
    {
        public Task<Result<IReadOnlyList<FaultJournalEntry>>> ReadAsync(CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<FaultJournalEntry>>.Success(entries));

        public Task<Result> WriteAsync(IReadOnlyList<FaultJournalEntry> items, CancellationToken ct) => Task.FromResult(Result.Success());
    }
}
