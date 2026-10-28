using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// L21 step 5: the runner checkpoints at run start and run end and carries the violations to the result (which the bundle writes).
/// </summary>
public sealed class RunnerCheckpointTests : IDisposable
{
    private const string SpecText = """
        schema = 1

        [section]
        id = "cp"
        title = "Checkpoint"
        requires = ["windows_containers"]

        [[row]]
        id = "r"
        title = "Row"
        expected = "skipped"
        depends_on = []
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "lab-l21-cp-" + Guid.NewGuid().ToString("N"));
    private readonly SecretVault _vault = new();
    private readonly AuditLog _audit;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly LeaseBook _leases;
    private readonly LabToolDispatcher _dispatcher;
    private readonly Redactor _redactor;

    public RunnerCheckpointTests()
    {
        Directory.CreateDirectory(_root);
        _redactor = new Redactor(_vault);
        _audit = new AuditLog(LabPaths.FromRoot(_root), new PathValidator(NullLogger<PathValidator>.Instance), _redactor, _time);
        _leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
        _dispatcher = new LabToolDispatcher(new LabToolRegistry(Array.Empty<ILabTool>()), _leases, _audit, _redactor, _time);
    }

    public void Dispose()
    {
        _audit.Dispose();
        _vault.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Run_CheckpointsAtStartAndEnd_ReportsViolationsAndRecordsLedger()
    {
        var invariant = new CountingInvariant();
        var ledger = new InvariantLedger();
        UatRunner runner = Runner(new InvariantMonitor([invariant]), ledger);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(Spec(), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess, run.Error);
        Assert.Equal(2, invariant.Calls);
        Assert.Equal(2, run.Value!.Invariants!.Violations.Count);
        Assert.Equal(2, ledger.Violations().Count);
    }

    [Fact]
    public async Task Run_WithoutMonitor_ReportsNoViolations()
    {
        UatRunner runner = Runner(null, null);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(Spec(), null, 1UL, null, null, PlanOnly: false), new NullSink(), CancellationToken.None);

        Assert.True(run.IsSuccess, run.Error);
        Assert.Empty(run.Value!.Invariants!.Violations);
    }

    private UatRunner Runner(InvariantMonitor? monitor, InvariantLedger? ledger) =>
        new(_dispatcher, _leases, new ClauseEvaluator(Array.Empty<IClauseSource>(), _redactor, _time), new NoProbe(), _time, invariants: monitor, ledger: ledger);

    private static UatSpec Spec()
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("cp.toml", SpecText);
        Assert.NotNull(loaded.Spec);
        return loaded.Spec!;
    }

    private sealed class CountingInvariant : IInvariant
    {
        public int Calls { get; private set; }

        public string Id => "I1";

        public Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
        {
            Calls++;
            IReadOnlyList<InvariantViolation> list = [new InvariantViolation("I1", "nodes", "unhandled", "planted", window.To, null)];
            return Task.FromResult(Result<IReadOnlyList<InvariantViolation>>.Success(list));
        }
    }

    private sealed class NoProbe : ICapabilityProbe
    {
        public Task<bool> IsAvailableAsync(string capability, CancellationToken ct) => Task.FromResult(false);
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
}
