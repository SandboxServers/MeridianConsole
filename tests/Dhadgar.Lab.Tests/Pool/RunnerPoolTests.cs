using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Pool;

/// <summary>
/// <c>requires_pool</c> in the runner (L30): one acquire per section, a reset before each row unless <c>reuse_member</c>,
/// release at section end, and <c>pool: &lt;error&gt;</c> skips.
/// </summary>
public sealed class RunnerPoolTests : IDisposable
{
    private const string Header = """
        schema = 1

        [section]
        id = "pool-run"
        title = "Pool run"
        requires_pool = { os = "linux", kind = "container" }

        """;

    private const string RowA = """
        [[row]]
        id = "a"
        title = "Row a"
        expected = "pass"
        depends_on = []

          [[row.step]]
          id = "read"
          tool = "stack_up"

          [[row.expect]]
          id = "read-ok"
          text = "read ok"
          source = "step"
          step = "read"
          pointer = "/result/rows/0"
          op = "exists"
          required = true

        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lab-l30-runner-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(PoolHarness.Start);
    private readonly FakePoolProvider _linux = new("linux-container", FakePoolProvider.LinuxContainer);
    private readonly PoolHarness _pool;
    private readonly AuditLog _audit;
    private readonly UatRunner _runner;
    private readonly RecordingPreparer _preparer = new();

    public RunnerPoolTests()
    {
        _ = Directory.CreateDirectory(_dir);
        _pool = new PoolHarness([_linux]);
        var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        _audit = new AuditLog(LabPaths.FromRoot(_dir), validator, redactor, _time);
        var leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
        var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new EchoTool() }), leases, _audit, redactor, _time);
        var evaluator = new ClauseEvaluator(new IClauseSource[] { new StepSource() }, redactor, _time);
        _runner = new UatRunner(dispatcher, leases, evaluator, new Probe(), _time, pool: _pool.Manager, preparer: _preparer);
    }

    public void Dispose()
    {
        _audit.Dispose();
        _pool.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Section_AcquiresOnce_ResetsBetweenRows_AndReleasesAtTheEnd()
    {
        string spec = Header + RowA + RowA.Replace("\"a\"", "\"b\"", StringComparison.Ordinal);

        UatRunResult run = await RunAsync(spec);

        Assert.All(run.Rows, row => Assert.Equal(RowGrade.Pass, row.Grade));
        Assert.All(run.Rows, row => Assert.Equal("lin-01", row.Member));
        Assert.Equal(["lin-01"], _linux.Provisioned);
        Assert.Equal(["lin-01"], _linux.Reset);
        Assert.Equal(["lin-01"], _linux.Destroyed);
    }

    [Fact]
    public async Task ReuseMember_SkipsTheResetBetweenRows()
    {
        // reuse_member is a row key: it goes before the first sub-table of the row.
        string second = RowA.Replace("\"a\"", "\"b\"", StringComparison.Ordinal).Replace("depends_on = []", "depends_on = []\nreuse_member = true", StringComparison.Ordinal);

        UatRunResult run = await RunAsync(Header + RowA + second);

        Assert.All(run.Rows, row => Assert.Equal(RowGrade.Pass, row.Grade));
        Assert.Empty(_linux.Reset);
        Assert.Equal(["lin-01"], _linux.Destroyed);
    }

    [Fact]
    public async Task Runner_ReleasesMemberWhenRowBlocks()
    {
        // The second row's reset fails, so the row blocks; the section still releases the member in its finally.
        _linux.FailReset = true;
        string second = RowA.Replace("\"a\"", "\"b\"", StringComparison.Ordinal);

        UatRunResult run = await RunAsync(Header + RowA + second);

        Assert.Equal(RowGrade.Pass, run.Rows[0].Grade);
        Assert.Equal(RowGrade.Blocked, run.Rows[1].Grade);
        Assert.Equal(["lin-01"], _linux.Destroyed);
    }

    [Fact]
    public async Task PoolFailure_SkipsEveryRowWithThePoolError()
    {
        _linux.Available = false;

        UatRunResult run = await RunAsync(Header + RowA);

        RowGrade grade = Assert.Single(run.Rows).Grade;
        Assert.Equal(RowGrade.Skipped, grade);
        Assert.Contains("pool: [Lab.Unavailable]", run.Rows[0].Reason, StringComparison.Ordinal);
        Assert.Empty(_linux.Provisioned);
    }

    [Fact]
    public async Task Section_PreparesTheMemberAfterAcquireAndAfterEachReset()
    {
        UatRunResult run = await RunAsync(Header + RowA + RowA.Replace("\"a\"", "\"b\"", StringComparison.Ordinal));

        Assert.All(run.Rows, row => Assert.Equal(RowGrade.Pass, row.Grade));
        Assert.Equal(["lin-01", "lin-01"], _preparer.Prepared);
    }

    [Fact]
    public async Task ReuseMember_PreparesOnlyOnce()
    {
        string second = RowA.Replace("\"a\"", "\"b\"", StringComparison.Ordinal).Replace("depends_on = []", "depends_on = []\nreuse_member = true", StringComparison.Ordinal);

        UatRunResult run = await RunAsync(Header + RowA + second);

        Assert.All(run.Rows, row => Assert.Equal(RowGrade.Pass, row.Grade));
        Assert.Equal(["lin-01"], _preparer.Prepared);
    }

    [Fact]
    public async Task PreparerFailure_SkipsEveryRowWithThePoolError_AndReleasesTheMember()
    {
        _preparer.FailWith = "[Lab.Docker] cli install failed";

        UatRunResult run = await RunAsync(Header + RowA + RowA.Replace("\"a\"", "\"b\"", StringComparison.Ordinal));

        Assert.All(run.Rows, row => Assert.Equal(RowGrade.Skipped, row.Grade));
        Assert.All(run.Rows, row => Assert.Contains("pool: [Lab.Docker] cli install failed", row.Reason, StringComparison.Ordinal));
        Assert.Equal(["lin-01"], _linux.Destroyed);
    }

    [Fact]
    public async Task SectionWithoutRequiresPool_NeverTouchesThePool()
    {
        string plain = Header.Replace("requires_pool = { os = \"linux\", kind = \"container\" }", string.Empty, StringComparison.Ordinal) + RowA;

        UatRunResult run = await RunAsync(plain);

        Assert.Equal(RowGrade.Pass, Assert.Single(run.Rows).Grade);
        Assert.Empty(_linux.Provisioned);
    }

    private async Task<UatRunResult> RunAsync(string specText)
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("pool-run.toml", specText);
        Assert.True(loaded.Spec is not null, string.Join("; ", loaded.Problems.Select(p => p.ToString())));
        Result<UatRunResult> run = await _runner.RunAsync(
            new UatRunRequest(loaded.Spec!, null, 7UL, null, null, PlanOnly: false), new CollectingSink(), CancellationToken.None);
        Assert.True(run.IsSuccess, run.Error);
        return run.Value!;
    }

    /// <summary>Records each member the runner prepares; fails with <see cref="FailWith"/> when it is set.</summary>
    private sealed class RecordingPreparer : IPoolMemberPreparer
    {
        public List<string> Prepared { get; } = [];

        public string? FailWith { get; set; }

        public Task<Result> PrepareAsync(PoolMember member, CancellationToken ct)
        {
            Prepared.Add(member.MemberId);
            return Task.FromResult(FailWith is null ? Result.Success() : Result.Failure(FailWith));
        }
    }

    private sealed class CollectingSink : IUatRunSink
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

    private sealed class Probe : ICapabilityProbe
    {
        public Task<bool> IsAvailableAsync(string capability, CancellationToken ct) => Task.FromResult(true);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EchoArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record EchoArgs(bool? Wipe = null);

    private sealed class EchoTool : LabTool<EchoArgs>
    {
        public override string Name => "stack_up";

        public override string Description => "runner pool test stack_up";

        protected override Task<Result<object>> ExecuteAsync(EchoArgs args, LabCallContext context, CancellationToken ct) =>
            Task.FromResult(Result<object>.Success(new { rows = new[] { new { a = 1 } } }));
    }
}
