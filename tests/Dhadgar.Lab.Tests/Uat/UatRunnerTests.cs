using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
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
        expected = "blocked: the fault tool is unregistered"
        depends_on = []

          [[row.fault]]
          id = "kill-it"
          inject = { tool = "fault_kill", args = { target = "x" } }
          start = "before_steps"
          heal = "after_steps"
        """;

    private const string BlockingStepSpec = """
        schema = 1

        [section]
        id = "runner-faults"
        title = "Runner faults"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "pass"
        depends_on = []

          [[row.fault]]
          id = "kill-it"
          inject = { tool = "fault_kill", args = { target = "x" } }
          start = "before_steps"
          heal = "after_steps"

          [[row.step]]
          id = "boom"
          tool = "unregistered_tool"
        """;

    private const string SeededFaultSpec = """
        schema = 1

        [section]
        id = "runner-seeded"
        title = "Runner seeded"

        [[row]]
        id = "garbled"
        title = "Garbled"
        expected = "pass"
        depends_on = []

          [[row.fault]]
          id = "garble"
          inject = { tool = "fault_corrupt_file", args = { file = "nodes-ca-key", mode = "garbage" } }
          start = "before_steps"
          heal = "after_steps"

          [[row.step]]
          tool = "faults_active"
        """;

    private const string SeededFaultSpecWithSeed = """
        schema = 1

        [section]
        id = "runner-seeded"
        title = "Runner seeded"

        [[row]]
        id = "garbled"
        title = "Garbled"
        expected = "pass"
        depends_on = []

          [[row.fault]]
          id = "garble"
          inject = { tool = "fault_corrupt_file", args = { file = "nodes-ca-key", mode = "garbage", seed = 5 } }
          start = "before_steps"
          heal = "after_steps"

          [[row.step]]
          tool = "faults_active"
        """;

    private const string CancelledStepSpec = """
        schema = 1

        [section]
        id = "runner-faults"
        title = "Runner faults"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "pass"
        depends_on = []

          [[row.fault]]
          id = "kill-it"
          inject = { tool = "fault_kill", args = { target = "x" } }
          start = "before_steps"
          heal = "after_steps"

          [[row.step]]
          id = "cancel"
          tool = "faults_active"
        """;

    private const string RunnerFailsSpec = """
        schema = 1

        [section]
        id = "runner-faults"
        title = "Runner faults"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "pass"
        depends_on = []

          [[row.fault]]
          id = "kill-it"
          inject = { tool = "fault_kill", args = { target = "x" } }
          start = "before_steps"
          heal = "after_steps"

          [[row.step]]
          id = "probe"
          tool = "probe_tool"
        """;

    private const string SecondInjectFailsSpec = """
        schema = 1

        [section]
        id = "runner-faults"
        title = "Runner faults"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "blocked"
        depends_on = []

          [[row.fault]]
          id = "first"
          inject = { tool = "fault_kill", args = { target = "x" } }
          start = "before_steps"
          heal = "after_steps"

          [[row.fault]]
          id = "second"
          inject = { tool = "fault_toxic_add", args = { target = "y" } }
          start = "before_steps"
          heal = "never"
        """;

    private const string SinceHealSpec = """
        schema = 1

        [section]
        id = "runner-faults"
        title = "Runner faults"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "pass"
        depends_on = []

          [[row.fault]]
          id = "kill-it"
          inject = { tool = "fault_kill", args = { target = "x" } }
          start = "before_steps"
          heal = "after_steps"

          [[row.expect]]
          id = "recovered"
          text = "recovered after heal"
          source = "probe"
          op = "truthy"
          since = "fault_heal:kill-it"
          required = true
        """;

    private const string HealFailsSpec = """
        schema = 1

        [section]
        id = "runner-faults"
        title = "Runner faults"

        [[row]]
        id = "faulted"
        title = "Faulted row"
        expected = "pass"
        depends_on = []

          [[row.fault]]
          id = "kill-it"
          inject = { tool = "fault_kill", args = { target = "x" } }
          start = "before_steps"
          heal = "after_steps"

        [[row]]
        id = "after"
        title = "Row after a failed heal"
        expected = "pass"
        depends_on = []
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
    public async Task FaultRow_UnregisteredFaultTool_IsBlocked()
    {
        using var harness = new Harness(new FakeProbe(true));

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(FaultSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.Equal(RowGrade.Blocked, row.Grade);
        Assert.Contains("fault kill-it", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FaultRow_StepBlocks_FaultStillHealed()
    {
        var kill = new ScriptedTool("fault_kill", () => Result<object>.Success(new { fault_id = "f-kill-1" }));
        var heal = new ScriptedTool("fault_heal", () => Result<object>.Success(new { healed = true }));
        using var harness = new Harness(new FakeProbe(true), null, kill, heal);

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(BlockingStepSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.Equal(RowGrade.Blocked, row.Grade);
        Assert.Single(kill.Calls);
        Assert.Equal("f-kill-1", Assert.Single(heal.Calls).FaultId);
    }

    [Fact(Timeout = 10000)]
    public async Task FaultRow_Cancelled_FaultStillHealed()
    {
        using var cts = new CancellationTokenSource();
        var kill = new ScriptedTool("fault_kill", () => Result<object>.Success(new { fault_id = "f-kill-1" }));
        var heal = new ScriptedTool("fault_heal", () => Result<object>.Success(new { healed = true }));
        var cancel = new ScriptedTool("faults_active", () =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        using var harness = new Harness(new FakeProbe(true), null, kill, heal, cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Runner.RunAsync(
            new UatRunRequest(Load(CancelledStepSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), cts.Token));

        Assert.Single(kill.Calls);
        Assert.Equal("f-kill-1", Assert.Single(heal.Calls).FaultId);
    }

    [Fact(Timeout = 10000)]
    public async Task FaultRow_RunnerThrowsMidRow_FaultStillHealedAndExceptionPropagates()
    {
        var kill = new ScriptedTool("fault_kill", () => Result<object>.Success(new { fault_id = "f-kill-1" }));
        var heal = new ScriptedTool("fault_heal", () => Result<object>.Success(new { healed = true }));
        var probe = new ScriptedTool("probe_tool", () => Result<object>.Success(new { ok = true }));
        // Appends: 1 inject begin, 2 inject complete, 3 probe step begin. The third throws once, outside the dispatcher.
        using var harness = new Harness(new FakeProbe(true), new ThrowOnceSink(failOnAppend: 3), kill, heal, probe);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Runner.RunAsync(
            new UatRunRequest(Load(RunnerFailsSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None));

        Assert.Single(kill.Calls);
        Assert.Empty(probe.Calls);
        Assert.Equal("f-kill-1", Assert.Single(heal.Calls).FaultId);
    }

    [Fact]
    public async Task FaultRow_InjectFails_IsBlockedAndOthersHealed()
    {
        var kill = new ScriptedTool("fault_kill", () => Result<object>.Success(new { fault_id = "f-kill-1" }));
        var heal = new ScriptedTool("fault_heal", () => Result<object>.Success(new { healed = true }));
        using var harness = new Harness(new FakeProbe(true), null, kill, heal);

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(SecondInjectFailsSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.Equal(RowGrade.Blocked, row.Grade);
        Assert.Contains("fault second", row.Reason, StringComparison.Ordinal);
        Assert.Single(kill.Calls);
        Assert.Equal("f-kill-1", Assert.Single(heal.Calls).FaultId);
    }

    [Fact]
    public async Task HealFails_LabNotClean_RemainingRowsBlocked()
    {
        var kill = new ScriptedTool("fault_kill", () => Result<object>.Success(new { fault_id = "f-kill-1" }));
        var heal = new ScriptedTool("fault_heal", () => Result<object>.Failure("[Lab.FaultHeal] heal refused"));
        var active = new ScriptedTool("faults_active", () => Result<object>.Success(new { faults = new[] { new { fault_id = "f-kill-1" } } }));
        using var harness = new Harness(new FakeProbe(true), null, kill, heal, active);

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(HealFailsSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(2, run.Value!.Rows.Count);
        Assert.Equal(RowGrade.Blocked, run.Value.Rows[0].Grade);
        Assert.Contains("heal kill-it", run.Value.Rows[0].Reason, StringComparison.Ordinal);
        Assert.Equal(RowGrade.Blocked, run.Value.Rows[1].Grade);
        Assert.Contains("lab not clean", run.Value.Rows[1].Reason, StringComparison.Ordinal);
        Assert.Single(kill.Calls);
    }

    [Fact]
    public async Task SinceFaultHeal_UsesHealTime()
    {
        var kill = new ScriptedTool("fault_kill", () => Result<object>.Success(new { fault_id = "f-kill-1" }));
        FakeTimeProvider? clock = null;
        var heal = new ScriptedTool("fault_heal", () =>
        {
            clock!.Advance(TimeSpan.FromSeconds(3));
            return Result<object>.Success(new { healed = true });
        });
        var source = new SinceProbeSource();
        using var harness = new Harness(new FakeProbe(true), null, null, [source], kill, heal);
        clock = harness.Time;

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(SinceHealSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(RowGrade.Pass, Assert.Single(run.Value!.Rows).Grade);
        Assert.NotNull(source.Since);
        Assert.True(source.Since!.Value > Start, "since = fault_heal must resolve to the heal moment, not the row start");
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

    [Fact]
    public async Task LeasedStep_BindsAndItsObservationNeverCarriesTheLeaseId()
    {
        var stack = new StackUpStub();
        var sink = new RecordingSink();
        using var harness = new Harness(new FakeProbe(true), sink, stack);

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(LeasedStepSpec), null, 3UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(1, stack.Invocations);
        Assert.Equal(true, stack.Wipe);
        Assert.False(string.IsNullOrEmpty(stack.LeaseId));
        Assert.NotEmpty(sink.Drafts);
        foreach (LabObservation draft in sink.Drafts)
        {
            string raw = draft.Args.GetRawText();
            Assert.DoesNotContain("lease_id", raw, StringComparison.Ordinal);
            Assert.DoesNotContain(stack.LeaseId!, raw, StringComparison.Ordinal);
        }
    }

    private const string LeasedStepSpec = """
        schema = 1

        [section]
        id = "runner-leased"
        title = "Runner leased step"

        [[row]]
        id = "leased-step"
        title = "Leased step"
        expected = "pass"
        depends_on = []

          [[row.step]]
          tool = "stack_up"
          args = { wipe = true }
        """;

    [Fact(Timeout = 10000)]
    public async Task WaitStep_RowIsNotDoneUntilTheWaitHasElapsed()
    {
        using var harness = new Harness(new FakeProbe(true));
        Task<Result<UatRunResult>> run = harness.Runner.RunAsync(
            new UatRunRequest(Load(WaitSpec), null, 5UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        // Real time lets the runner reach the wait; the fake clock has not moved, so the row cannot be done yet.
        await Task.Delay(50);
        Assert.False(run.IsCompleted);

        harness.Time.Advance(TimeSpan.FromSeconds(60));
        Result<UatRunResult> result = await run;

        Assert.True(result.IsSuccess);
    }

    private const string WaitSpec = """
        schema = 1

        [section]
        id = "runner-wait"
        title = "Runner wait"

        [[row]]
        id = "waits"
        title = "Waits"
        expected = "pass"
        depends_on = []

          [[row.step]]
          wait_ms = 60000
        """;

    private const string InvariantSpec = """
        schema = 1

        [section]
        id = "runner-invariants"
        title = "Runner invariants"

        [[row]]
        id = "strict"
        title = "Strict"
        expected = "no violation"
        depends_on = []

          [[row.step]]
          tool = "faults_active"

        [[row]]
        id = "lenient"
        title = "Lenient"
        expected = "allowed violation"
        depends_on = []
        allow_invariants = ["I1"]

          [[row.step]]
          tool = "faults_active"
        """;

    [Fact]
    public async Task Violation_NotAllowed_FailsPassRow()
    {
        var invariant = new ScriptedInvariant("I1", window => window.Row == "strict" ? Violated("I1", window) : Clean());
        using var harness = new Harness(new FakeProbe(true), null, new InvariantMonitor([invariant]), ProbeTool());

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(InvariantSpec), new[] { "strict" }, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.Equal(RowGrade.Fail, row.Grade);
        Assert.Contains("I1", row.Reason, StringComparison.Ordinal);
        Assert.False(Assert.Single(row.Violations!).Allowed);
        Assert.Equal(RunOverall.Fail, run.Value.Overall);
    }

    [Fact]
    public async Task Violation_Allowed_RecordedNotFailed()
    {
        var invariant = new ScriptedInvariant("I1", window => window.Row == "lenient" ? Violated("I1", window) : Clean());
        using var harness = new Harness(new FakeProbe(true), null, new InvariantMonitor([invariant]), ProbeTool());

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(InvariantSpec), new[] { "lenient" }, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.NotEqual(RowGrade.Fail, row.Grade);
        UatRowViolation violation = Assert.Single(row.Violations!);
        Assert.Equal("I1", violation.Violation.InvariantId);
        Assert.True(violation.Allowed);
    }

    [Fact]
    public async Task CheckError_InRow_IsUnverified()
    {
        var invariant = new ScriptedInvariant("I1", window => window.Row == "strict" ? Result<IReadOnlyList<InvariantViolation>>.Failure("source down") : Clean());
        using var harness = new Harness(new FakeProbe(true), null, new InvariantMonitor([invariant]), ProbeTool());

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(InvariantSpec), new[] { "strict" }, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.Equal(RowGrade.Unverified, row.Grade);
        Assert.StartsWith("invariant I1 unchecked", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Violation_OutsideRows_SyntheticRow()
    {
        var invariant = new ScriptedInvariant("I1", window => window.Row is null ? Violated("I1", window) : Clean());
        using var harness = new Harness(new FakeProbe(true), null, new InvariantMonitor([invariant]), ProbeTool());

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(InvariantSpec), new[] { "strict" }, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        UatRowResult synthetic = Assert.Single(run.Value!.Rows, r => r.RowId == "invariants/I1");
        Assert.Equal(RowGrade.Fail, synthetic.Grade);
        Assert.Equal(2, synthetic.Violations!.Count);
        Assert.Equal(RunOverall.Fail, run.Value.Overall);
    }

    [Fact]
    public async Task Skips_AreLabelledByCheckpoint_AndNeverGrade()
    {
        // The strict row has no expect clauses, so it grades Unverified on its own: the grade must equal the clean-invariant baseline.
        using var clean = new Harness(new FakeProbe(true), null, new InvariantMonitor([new ScriptedInvariant("I2", _ => Clean())]), ProbeTool());
        Result<UatRunResult> baseline = await clean.Runner.RunAsync(
            new UatRunRequest(Load(InvariantSpec), new[] { "strict" }, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        var invariant = new SkippingInvariant("I2");
        using var harness = new Harness(new FakeProbe(true), null, new InvariantMonitor([invariant]), ProbeTool());

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(InvariantSpec), new[] { "strict" }, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        UatRowResult row = Assert.Single(run.Value!.Rows);
        UatRowResult baselineRow = Assert.Single(baseline.Value!.Rows);
        Assert.Equal(baselineRow.Grade, row.Grade);
        Assert.Equal(baselineRow.Reason, row.Reason);
        Assert.NotEqual(RowGrade.Fail, row.Grade);
        Assert.Empty(run.Value.Invariants!.Errors);
        Assert.Equal(["(run-start)", "strict", "(run-end)"], run.Value.Invariants!.Skipped!.Select(s => s.Row));
        Assert.DoesNotContain(run.Value.Rows, r => r.RowId.StartsWith("invariants/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeededFault_SeedIsDerivedFromTheRunSeed()
    {
        ulong[] seeds = [1UL, 1UL, 2UL];
        var recorded = new List<ulong?>();
        foreach (ulong runSeed in seeds)
        {
            var corrupt = new ScriptedTool("fault_corrupt_file", () => Result<object>.Success(new { fault_id = "f-garble-1" }));
            var heal = new ScriptedTool("fault_heal", () => Result<object>.Success(new { healed = true }));
            using var harness = new Harness(new FakeProbe(true), null, corrupt, heal, ProbeTool());

            Result<UatRunResult> run = await harness.Runner.RunAsync(
                new UatRunRequest(Load(SeededFaultSpec), new[] { "garbled" }, runSeed, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

            Assert.True(run.IsSuccess);
            recorded.Add(Assert.Single(corrupt.Calls).Seed);
        }

        Assert.All(recorded, seed => Assert.NotNull(seed));
        Assert.Equal(recorded[0], recorded[1]);
        Assert.NotEqual(recorded[0], recorded[2]);
    }

    [Fact]
    public async Task SeededFault_SpecSeed_IsKept()
    {
        var corrupt = new ScriptedTool("fault_corrupt_file", () => Result<object>.Success(new { fault_id = "f-garble-1" }));
        var heal = new ScriptedTool("fault_heal", () => Result<object>.Success(new { healed = true }));
        using var harness = new Harness(new FakeProbe(true), null, corrupt, heal, ProbeTool());

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(SeededFaultSpecWithSeed), new[] { "garbled" }, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(5UL, Assert.Single(corrupt.Calls).Seed);
    }

    private static Result<IReadOnlyList<InvariantViolation>> Clean() => Result<IReadOnlyList<InvariantViolation>>.Success([]);

    private static Result<IReadOnlyList<InvariantViolation>> Violated(string id, InvariantWindow window) =>
        Result<IReadOnlyList<InvariantViolation>>.Success([new InvariantViolation(id, "nodes", "check-" + id, "redacted", window.To, null)]);

    private static ScriptedTool ProbeTool() => new("faults_active", () => Result<object>.Success(new { faults = Array.Empty<object>() }));

    private sealed class ScriptedInvariant(string id, Func<InvariantWindow, Result<IReadOnlyList<InvariantViolation>>> answer) : IInvariant
    {
        public string Id { get; } = id;

        public Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct) => Task.FromResult(answer(window));
    }

    private sealed class SkippingInvariant(string id) : ISkippableInvariant
    {
        public string Id { get; } = id;

        public Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct) => Task.FromResult(Clean());

        public Task<Result<InvariantOutcome>> CheckWithSkipsAsync(InvariantWindow window, CancellationToken ct) =>
            Task.FromResult(Result<InvariantOutcome>.Success(new InvariantOutcome(
                [],
                [new InvariantSkip(Id, "db:nodes", "stack not up (no lab credentials)")])));
    }

    private static UatSpec Load(string text)
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("runner-selftest.toml", text);
        Assert.NotNull(loaded.Spec);
        return loaded.Spec!;
    }

    private sealed class SinceProbeSource : IClauseSource
    {
        public string Name => "probe";

        public DateTimeOffset? Since { get; private set; }

        public Task<Result<System.Text.Json.JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
        {
            Result<DateTimeOffset> since = window.SinceFor(clause.Since ?? string.Empty);
            if (since.IsSuccess)
            {
                Since = since.Value;
            }

            return Task.FromResult(Result<System.Text.Json.JsonElement>.Success(System.Text.Json.JsonDocument.Parse("true").RootElement.Clone()));
        }
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "StubArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record StubArgs(bool? Wipe = null);

    /// <summary>
    /// A real Leased lab tool under the name <c>stack_up</c>. Records what it bound and the lease id its call context carried.
    /// </summary>
    private sealed class StackUpStub : LabTool<StubArgs>
    {
        public override string Name => "stack_up";

        public override string Description => "test stack_up";

        public int Invocations { get; private set; }

        public bool? Wipe { get; private set; }

        public string? LeaseId { get; private set; }

        protected override Task<Result<object>> ExecuteAsync(StubArgs args, LabCallContext context, CancellationToken ct)
        {
            Invocations++;
            Wipe = args.Wipe;
            LeaseId = context.LeaseId;
            return Task.FromResult(Result<object>.Success(new { ok = true }));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "ScriptedArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record ScriptedArgs(string? Target = null, string? FaultId = null, string? File = null, string? Mode = null, ulong? Seed = null);

    /// <summary>
    /// A tool under a given name (a fault or check tool) that answers with the scripted result and keeps every call's arguments.
    /// </summary>
    private sealed class ScriptedTool : LabTool<ScriptedArgs>
    {
        private readonly string _name;
        private readonly Func<Result<object>> _answer;

        public ScriptedTool(string name, Func<Result<object>> answer)
        {
            _name = name;
            _answer = answer;
        }

        public override string Name => _name;

        public override string Description => "test " + _name;

        public List<ScriptedArgs> Calls { get; } = [];

        protected override Task<Result<object>> ExecuteAsync(ScriptedArgs args, LabCallContext context, CancellationToken ct)
        {
            Calls.Add(args);
            return Task.FromResult(_answer());
        }
    }

    /// <summary>
    /// Keeps every observation draft the runner appends, so a test can read what was recorded.
    /// </summary>
    private sealed class RecordingSink : ILabObservationSink
    {
        private long _seq;

        public List<LabObservation> Drafts { get; } = [];

        public Result BeginRun(string runId, string runFolder) => Result.Success();

        public Task<Result<long>> EndRunAsync(string runId, CancellationToken ct) => Task.FromResult(Result<long>.Success(0));

        public OpId NextOp(string runId, string actor) => new(runId, actor, ++_seq);

        public Result<long> Append(LabObservation draft)
        {
            Drafts.Add(draft);
            return Result<long>.Success(Drafts.Count);
        }

        public IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max) => Array.Empty<LabObservation>();
    }

    /// <summary>
    /// Records like <see cref="RecordingSink"/>, but throws once on the given Append call, the way a runner-side failure outside the dispatcher would.
    /// </summary>
    private sealed class ThrowOnceSink(int failOnAppend) : ILabObservationSink
    {
        private readonly RecordingSink _inner = new();
        private int _appends;

        public Result BeginRun(string runId, string runFolder) => _inner.BeginRun(runId, runFolder);

        public Task<Result<long>> EndRunAsync(string runId, CancellationToken ct) => _inner.EndRunAsync(runId, ct);

        public OpId NextOp(string runId, string actor) => _inner.NextOp(runId, actor);

        public Result<long> Append(LabObservation draft)
        {
            _appends++;
            if (_appends == failOnAppend)
            {
                throw new InvalidOperationException("sink failed on append " + _appends.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return _inner.Append(draft);
        }

        public IReadOnlyList<LabObservation> Read(string runId, long afterSeq, int max) => _inner.Read(runId, afterSeq, max);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _root;
        private readonly SecretVault _vault;
        private readonly AuditLog _audit;

        public Harness(ICapabilityProbe probe, ILabObservationSink? observations = null, params ILabTool[] tools)
            : this(probe, observations, null, tools)
        {
        }

        public Harness(ICapabilityProbe probe, ILabObservationSink? observations, InvariantMonitor? invariants, params ILabTool[] tools)
            : this(probe, observations, invariants, Array.Empty<IClauseSource>(), tools)
        {
        }

        public Harness(ICapabilityProbe probe, ILabObservationSink? observations, InvariantMonitor? invariants, IClauseSource[] sources, params ILabTool[] tools)
        {
            _root = Path.Combine(Path.GetTempPath(), "lab-l15-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            var time = new FakeTimeProvider(Start);
            Time = time;
            _vault = new SecretVault();
            var redactor = new Redactor(_vault);
            _audit = new AuditLog(LabPaths.FromRoot(_root), new PathValidator(NullLogger<PathValidator>.Instance), redactor, time);
            Leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
            var dispatcher = new LabToolDispatcher(new LabToolRegistry(tools), Leases, _audit, redactor, time);
            Runner = new UatRunner(dispatcher, Leases, new ClauseEvaluator(sources, redactor, time), probe, time, observations, invariants: invariants, settle: TimeSpan.Zero);
        }

        public LeaseBook Leases { get; }

        public UatRunner Runner { get; }

        public FakeTimeProvider Time { get; }

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
