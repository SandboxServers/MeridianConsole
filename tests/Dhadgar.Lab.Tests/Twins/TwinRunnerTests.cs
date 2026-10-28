using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Twins;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Twins;

/// <summary>
/// The twin runner (L53, C19) with fakes: the base world runs first, the variant second, the synthetic <c>twin/&lt;id&gt;</c> row reaches
/// both sinks before either run finishes, and a base row that is not PASS makes the relation incomplete, never held.
/// </summary>
public sealed class TwinRunnerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private const string SpecFile = "twin-selftest.toml";

    private const string SelfTestSpec = """
        schema = 1

        [section]
        id = "twin-selftest"
        title = "Twin self-test"

        [[row]]
        id = "main"
        title = "Main row"
        expected = "pass"
        depends_on = []

          [[row.step]]
          id = "touch"
          tool = "stack_up"

          [[row.expect]]
          id = "reached"
          text = "the row reached its clauses"
          source = "probe"
          op = "truthy"
          required = true
        """;

    private const string TwinToml = """
        schema = 1
        id = "twin-selftest"
        relation = "equal_outcome"
        seed = 7
        [base]
        spec = "twin-selftest.toml"
        rows = ["main"]
        [variant]
        spec = "twin-selftest.toml"
        rows = ["main"]
        [[compare]]
        pointer = "/rows/main/grade"
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l53-twins-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(Start);
    private readonly SecretVault _vault = new();
    private readonly Redactor _redactor;
    private readonly PathValidator _validator;
    private readonly AuditLog _audit;
    private readonly LeaseBook _leases;
    private readonly List<string> _events = [];

    public TwinRunnerTests()
    {
        _ = Directory.CreateDirectory(_root);
        _redactor = new Redactor(_vault);
        _validator = new PathValidator(NullLogger<PathValidator>.Instance);
        _audit = new AuditLog(LabPaths.FromRoot(_root), _validator, _redactor, _time);
        _leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
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
    public async Task Twin_PreconditionFailed_IsIncompleteNotPass()
    {
        // The first stack_up (the base world's step) fails; the variant's step succeeds.
        int calls = 0;
        var touch = new TwinScriptedTool("stack_up", () => ++calls == 1 ? Result<object>.Failure("[Lab.Unavailable] base step failed") : Record("step"));
        TwinRunner runner = Runner(touch);
        var baseSink = new RecordingSink("base");
        var variantSink = new RecordingSink("variant");

        Result<TwinRunResult> run = await runner.RunAsync(Experiment(), Spec(), Spec(), baseSink, variantSink, null, null, null, CancellationToken.None);

        Assert.True(run.IsSuccess, run.Error);
        Assert.Null(run.Value!.Held);
        Assert.Equal(RowGrade.Skipped, run.Value.TwinRow.Grade);
        Assert.StartsWith("twin precondition: main", run.Value.TwinRow.Reason, StringComparison.Ordinal);
        Assert.Equal(2, calls);
        Assert.Equal("twin/twin-selftest", run.Value.TwinRow.RowId);
    }

    [Fact]
    public async Task Twin_NotHeld_TwinSignatureOnBothRuns()
    {
        // Both worlds run their step; the variant's step fails, so the two projections differ.
        int calls = 0;
        var touch = new TwinScriptedTool("stack_up", () => ++calls == 2 ? Result<object>.Failure("[Lab.Unavailable] variant step failed") : Record("step"));
        TwinRunner runner = Runner(touch);
        var baseSink = new RecordingSink("base");
        var variantSink = new RecordingSink("variant");

        Result<TwinRunResult> run = await runner.RunAsync(Experiment(), Spec(), Spec(), baseSink, variantSink, null, null, null, CancellationToken.None);

        Assert.True(run.IsSuccess, run.Error);
        Assert.False(run.Value!.Held);
        Assert.Equal(RowGrade.Fail, run.Value.TwinRow.Grade);
        Assert.Contains(run.Value.Diffs, d => d.Locator == "/rows/main/grade");
        foreach (RecordingSink sink in new[] { baseSink, variantSink })
        {
            string[] tail = sink.Events.Where(e => e.StartsWith("row:twin", StringComparison.Ordinal) || e == "finish").ToArray();
            Assert.Equal(new[] { "row:twin/twin-selftest", "finish" }, tail);
            Assert.Equal("twin/twin-selftest", sink.Finished!.Rows[^1].RowId);
            Assert.Equal(RowGrade.Fail, sink.Finished.Rows[^1].Grade);
        }

        Assert.Equal(RunOverall.Fail, run.Value.Overall);
        Assert.Equal(run.Value.Variant.RunId, baseSink.Finished!.Pair!.PartnerRunId);
        Assert.Equal(run.Value.Base.RunId, variantSink.Finished!.Pair!.PartnerRunId);
    }

    [Fact]
    public async Task Twin_Held_WhenBothWorldsAgree()
    {
        var touch = new TwinScriptedTool("stack_up", () => Record("step"));
        TwinRunner runner = Runner(touch);

        Result<TwinRunResult> run = await runner.RunAsync(Experiment(), Spec(), Spec(), new RecordingSink("base"), new RecordingSink("variant"), null, null, null, CancellationToken.None);

        Assert.True(run.IsSuccess, run.Error);
        Assert.True(run.Value!.Held);
        Assert.Equal(RowGrade.Pass, run.Value.TwinRow.Grade);
        Assert.Empty(run.Value.Diffs);
        Assert.Equal(2, touch.Calls.Count);
    }

    private static TwinSpec Experiment()
    {
        Result<TwinSpec> loaded = TwinSpecLoader.Load(TwinToml);
        Assert.True(loaded.IsSuccess, loaded.Error);
        return loaded.Value!;
    }

    private static UatSpec Spec()
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText(SpecFile, SelfTestSpec);
        Assert.NotNull(loaded.Spec);
        return loaded.Spec!;
    }

    private TwinRunner Runner(params ILabTool[] tools)
    {
        var dispatcher = new LabToolDispatcher(new LabToolRegistry([.. tools, new TwinScriptedTool("stack_reset_data", () => Record("reset"))]), _leases, _audit, _redactor, _time);
        var uat = new UatRunner(dispatcher, _leases, new ClauseEvaluator([new TruthySource()], _redactor, _time), new FakeProbe(), _time, settle: TimeSpan.Zero);
        return new TwinRunner(uat, dispatcher, _leases, _time);
    }

    private Result<object> Record(string evt, object? payload = null)
    {
        _events.Add(evt);
        return Result<object>.Success(payload ?? new { ok = true });
    }

    /// <summary>
    /// The <c>probe</c> clause source: every read is <c>true</c>, so a row that reaches its clauses holds them.
    /// </summary>
    private sealed class TruthySource : IClauseSource
    {
        public string Name => "probe";

        public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct) =>
            Task.FromResult(Result<JsonElement>.Success(JsonDocument.Parse("true").RootElement.Clone()));
    }

    private sealed class FakeProbe : ICapabilityProbe
    {
        public Task<bool> IsAvailableAsync(string capability, CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>
    /// A sink that keeps the order of its calls and the finished result.
    /// </summary>
    private sealed class RecordingSink(string name) : IUatRunSink
    {
        public List<string> Events { get; } = [];

        public UatRunResult? Finished { get; private set; }

        public void RunStarted(string runId, ulong seed, UatSpec spec) => Events.Add("start:" + name);

        public void RowFinished(UatRowResult row) => Events.Add("row:" + row.RowId);

        public void RunFinished(UatRunResult result)
        {
            Finished = result;
            Events.Add("finish");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "TwinArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record TwinArgs(string? Target = null, string? FaultId = null);

    /// <summary>
    /// A tool under a given name that answers with the scripted result and keeps every call's arguments.
    /// </summary>
    private sealed class TwinScriptedTool : LabTool<TwinArgs>
    {
        private readonly string _name;
        private readonly Func<Result<object>> _answer;

        public TwinScriptedTool(string name, Func<Result<object>> answer)
        {
            _name = name;
            _answer = answer;
        }

        public override string Name => _name;

        public override string Description => "test " + _name;

        public List<TwinArgs> Calls { get; } = [];

        protected override Task<Result<object>> ExecuteAsync(TwinArgs args, LabCallContext context, CancellationToken ct)
        {
            Calls.Add(args);
            return Task.FromResult(_answer());
        }
    }
}
