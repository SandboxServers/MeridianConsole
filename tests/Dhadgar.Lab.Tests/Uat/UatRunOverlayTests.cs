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
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// The twin overlay of a run (L53, C19): pre rows run first and are graded and recorded; extra faults inject before the steps of the other rows and
/// heal at row end through <see cref="RowExecutor"/>; the pair reaches <c>run.json</c>.
/// </summary>
public sealed class UatRunOverlayTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private const string OverlaySpecFile = "overlay-selftest.toml";

    private const string OverlaySpec = """
        schema = 1

        [section]
        id = "overlay-selftest"
        title = "Overlay self-test"

        [[row]]
        id = "pre"
        title = "Pre row"
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

        [[row]]
        id = "main"
        title = "Main row"
        expected = "pass"
        depends_on = ["pre"]

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

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l53-overlay-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(Start);
    private readonly SecretVault _vault = new();
    private readonly Redactor _redactor;
    private readonly PathValidator _validator;
    private readonly AuditLog _audit;
    private readonly LeaseBook _leases;
    private readonly List<string> _events = [];

    public UatRunOverlayTests()
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
    public async Task PreRows_RunFirst_AndTheirGradeGatesTheRest()
    {
        var touch = new ScriptedTool("stack_up", () => Record("step"));
        UatRunner runner = Runner(touch);
        var overlay = new UatRunOverlay(Array.Empty<FaultSpec>(), OverlaySpecFile, ["pre"], null);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(Load(OverlaySpec), new[] { "main" }, 1UL, null, null, PlanOnly: false), new NullSink(), overlay, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(["pre", "main"], run.Value!.Rows.Select(r => r.RowId));
        Assert.Equal(RowGrade.Pass, run.Value.Rows[0].Grade);
        Assert.Equal(RowGrade.Pass, run.Value.Rows[1].Grade);
        Assert.Equal(2, touch.Calls.Count);
    }

    [Fact]
    public async Task ExtraFaults_InjectBeforeSteps_AndHealAtRowEnd()
    {
        var inject = new ScriptedTool("fault_kill", () => Record("inject", new { fault_id = "f-kill-1" }));
        var touch = new ScriptedTool("stack_up", () => Record("step"));
        var heal = new ScriptedTool("fault_heal", () => Record("heal", new { healed = true }));
        UatRunner runner = Runner(inject, touch, heal);
        JsonElement args = JsonSerializer.SerializeToElement(new { target = "x" });
        var overlay = new UatRunOverlay([new FaultSpec("fault_kill", args)], null, Array.Empty<string>(), null);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(Load(OverlaySpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), overlay, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(["inject", "step", "heal", "inject", "step", "heal"], _events);
        Assert.Equal("x", inject.Calls[0].Target);
        Assert.Equal(["f-kill-1", "f-kill-1"], heal.Calls.Select(c => c.FaultId));
        UatRowFault fault = Assert.Single(run.Value!.Rows[0].Faults!);
        Assert.Equal("fault_kill", fault.Tool);
        Assert.NotNull(fault.HealedAt);
        Assert.Null(fault.HealError);
    }

    [Fact]
    public async Task Pair_GoesToRunJson()
    {
        string uatRuns = Path.Combine(_root, "uat-runs");
        _ = Directory.CreateDirectory(uatRuns);
        var writer = new BundleWriter(uatRuns, _validator, _redactor, new SecretScanner(_vault), _time, "abcd1234", "linux", "build", null);
        var touch = new ScriptedTool("stack_up", () => Record("step"));
        UatRunner runner = Runner(touch);
        var pair = new PairInfo("enroll-latency-invariance", "variant", "partner-run-1", "equal_outcome");
        var overlay = new UatRunOverlay(Array.Empty<FaultSpec>(), null, Array.Empty<string>(), pair);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(Load(OverlaySpec), new[] { "main" }, 1UL, null, null, PlanOnly: false), writer, overlay, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(pair, run.Value!.Pair);
        Assert.NotNull(writer.Folder);
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "run.json")));
        JsonElement written = json.RootElement.GetProperty("pair");
        Assert.Equal("enroll-latency-invariance", written.GetProperty("experiment_id").GetString());
        Assert.Equal("variant", written.GetProperty("role").GetString());
        Assert.Equal("partner-run-1", written.GetProperty("partner_run_id").GetString());
        Assert.Equal("equal_outcome", written.GetProperty("relation").GetString());
    }

    [Fact]
    public async Task PreSpec_OfAnotherFile_IsRefused()
    {
        UatRunner runner = Runner(new ScriptedTool("stack_up", () => Record("step")));
        var overlay = new UatRunOverlay(Array.Empty<FaultSpec>(), "other.toml", ["pre"], null);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(Load(OverlaySpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), overlay, CancellationToken.None);

        Assert.True(run.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", run.Error, StringComparison.Ordinal);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task PreRow_NotInTheSpec_IsRefused()
    {
        UatRunner runner = Runner(new ScriptedTool("stack_up", () => Record("step")));
        var overlay = new UatRunOverlay(Array.Empty<FaultSpec>(), OverlaySpecFile, ["nope"], null);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(Load(OverlaySpec), null, 1UL, null, null, PlanOnly: false), new NullSink(), overlay, CancellationToken.None);

        Assert.True(run.IsFailure);
        Assert.Contains("[Lab.NotFound]", run.Error, StringComparison.Ordinal);
        Assert.Empty(_events);
    }

    private UatRunner Runner(params ILabTool[] tools)
    {
        var dispatcher = new LabToolDispatcher(new LabToolRegistry(tools), _leases, _audit, _redactor, _time);
        return new UatRunner(dispatcher, _leases, new ClauseEvaluator([new TruthySource()], _redactor, _time), new FakeProbe(), _time, settle: TimeSpan.Zero);
    }

    private Result<object> Record(string evt, object? payload = null)
    {
        _events.Add(evt);
        return Result<object>.Success(payload ?? new { ok = true });
    }

    private static UatSpec Load(string text)
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText(OverlaySpecFile, text);
        Assert.NotNull(loaded.Spec);
        return loaded.Spec!;
    }

    /// <summary>
    /// The <c>probe</c> clause source: every read is <c>true</c>, so a row that reaches its clauses holds them.
    /// </summary>
    private sealed class TruthySource : IClauseSource
    {
        public string Name => "probe";

        public Task<Result<System.Text.Json.JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct) =>
            Task.FromResult(Result<System.Text.Json.JsonElement>.Success(JsonDocument.Parse("true").RootElement.Clone()));
    }

    private sealed class FakeProbe : ICapabilityProbe
    {
        public Task<bool> IsAvailableAsync(string capability, CancellationToken ct) => Task.FromResult(true);
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "OverlayArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record OverlayArgs(string? Target = null, string? FaultId = null);

    /// <summary>
    /// A tool under a given name that answers with the scripted result and keeps every call's arguments.
    /// </summary>
    private sealed class ScriptedTool : LabTool<OverlayArgs>
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

        public List<OverlayArgs> Calls { get; } = [];

        protected override Task<Result<object>> ExecuteAsync(OverlayArgs args, LabCallContext context, CancellationToken ct)
        {
            Calls.Add(args);
            return Task.FromResult(_answer());
        }
    }
}
