using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

public sealed class UatRunnerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private const string ContainersSpec = """
        schema = 1

        [section]
        id = "runner-selftest"
        title = "Runner self-test"
        requires = ["windows_containers"]

        [[row]]
        id = "needs-containers"
        title = "Needs containers"
        expected = "skipped"
        depends_on = []
        """;

    private const string FaultSpec = """
        schema = 1

        [section]
        id = "runner-faults"
        title = "Runner faults"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "blocked until L22"
        depends_on = []

          [[row.fault]]
          id = "kill-it"
          inject = { tool = "fault_kill", args = { target = "x" } }
          start = "before_steps"
          heal = "after_steps"
        """;

    [Fact]
    public async Task PlanOnly_TakesNoLease_AndReportsTheRequestedSeed()
    {
        using var harness = new Harness(new FakeProbe(true));
        UatSpec spec = Load(ContainersSpec);

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(spec, null, 42UL, null, null, PlanOnly: true), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.True(run.Value!.PlanOnly);
        Assert.Equal(42UL, run.Value.Seed);
        Assert.Empty(run.Value.Rows);
        Assert.Null(harness.Leases.Current);
    }

    [Fact]
    public async Task HeldLease_RefusesTheRunBeforeAnyRow()
    {
        using var harness = new Harness(new FakeProbe(true));
        Result<LeaseGrant> held = harness.Leases.Acquire("someone-else", "held elsewhere", null, false, null);
        Assert.True(held.IsSuccess);

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(ContainersSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsFailure);
        Assert.Contains("Lab.LeaseHeld", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnLease_IsReleasedAfterTheRun()
    {
        using var harness = new Harness(new FakeProbe(true));

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(ContainersSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Null(harness.Leases.Current);
    }

    [Fact]
    public async Task MissingCapability_SkipsTheRowAndNamesTheCapability()
    {
        using var harness = new Harness(new FakeProbe(false));

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(ContainersSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.Equal(RowGrade.Skipped, row.Grade);
        Assert.Contains("windows_containers", row.Reason, StringComparison.Ordinal);
        Assert.Equal(RunOverall.Incomplete, run.Value.Overall);
    }

    [Fact]
    public async Task FaultRow_IsBlockedUntilL22()
    {
        using var harness = new Harness(new FakeProbe(true));

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(FaultSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.Equal(RowGrade.Blocked, row.Grade);
        Assert.Contains("L22", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitSeed_IsReportedUnchangedAcrossRuns()
    {
        using var harness = new Harness(new FakeProbe(true));
        UatSpec spec = Load(ContainersSpec);

        Result<UatRunResult> first = await harness.Runner.RunAsync(
            new UatRunRequest(spec, null, 7UL, null, null, PlanOnly: true), new NullSink(), CancellationToken.None);
        Result<UatRunResult> second = await harness.Runner.RunAsync(
            new UatRunRequest(spec, null, 7UL, null, null, PlanOnly: true), new NullSink(), CancellationToken.None);

        Assert.Equal(7UL, first.Value!.Seed);
        Assert.Equal(first.Value.Seed, second.Value!.Seed);
    }

    private static UatSpec Load(string text)
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("runner-selftest.toml", text);
        Assert.NotNull(loaded.Spec);
        return loaded.Spec!;
    }

    private sealed class FakeProbe : ICapabilityProbe
    {
        private readonly bool _answer;

        public FakeProbe(bool answer) => _answer = answer;

        public Task<bool> IsAvailableAsync(string capability, CancellationToken ct) => Task.FromResult(_answer);
    }

    private sealed class NullSink : IUatRunSink
    {
        public void RunStarted(string runId, ulong seed, UatSpec spec)
        {
        }

        public void RowFinished(UatRowResult row)
        {
        }

        public void RunFinished(UatRunResult result)
        {
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _root;
        private readonly SecretVault _vault;
        private readonly AuditLog _audit;

        public Harness(ICapabilityProbe probe)
        {
            _root = Path.Combine(Path.GetTempPath(), "lab-l15-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            var time = new FakeTimeProvider(Start);
            _vault = new SecretVault();
            var redactor = new Redactor(_vault);
            _audit = new AuditLog(LabPaths.FromRoot(_root), new PathValidator(NullLogger<PathValidator>.Instance), redactor, time);
            Leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
            var dispatcher = new LabToolDispatcher(new LabToolRegistry(Array.Empty<ILabTool>()), Leases, _audit, redactor, time);
            Runner = new UatRunner(dispatcher, Leases, new ClauseEvaluator(Array.Empty<IClauseSource>(), redactor, time), probe, time);
        }

        public LeaseBook Leases { get; }

        public UatRunner Runner { get; }

        public void Dispose()
        {
            _audit.Dispose();
            _vault.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
