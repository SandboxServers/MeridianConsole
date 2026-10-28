using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
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
        expected = "blocked until L22"
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
    public async Task FaultRow_IsBlockedUntilL22()
    {
        using var harness = new Harness(new FakeProbe(true));

        Result<UatRunResult> run = await harness.Runner.RunAsync(
            new UatRunRequest(Load(FaultSpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess);
        UatRowResult row = Assert.Single(run.Value!.Rows);
        Assert.Equal(RowGrade.Blocked, row.Grade);
        // D-ML73: superseded placeholder assertion. The fault tool is unregistered here, so the inject fails and names the fault.
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
    private sealed record ScriptedArgs(string? Target = null, string? FaultId = null);

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

    private sealed class Harness : IDisposable
    {
        private readonly string _root;
        private readonly SecretVault _vault;
        private readonly AuditLog _audit;

        public Harness(ICapabilityProbe probe, ILabObservationSink? observations = null, params ILabTool[] tools)
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
            Runner = new UatRunner(dispatcher, Leases, new ClauseEvaluator(Array.Empty<IClauseSource>(), redactor, time), probe, time, observations, settle: TimeSpan.Zero);
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
