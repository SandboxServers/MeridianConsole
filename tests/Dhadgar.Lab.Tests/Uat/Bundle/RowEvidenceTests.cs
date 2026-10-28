using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Bundle;

/// <summary>
/// The C9 row evidence: observed values, attempts, checked_at and step records reach the row file, while the uat_run result stays evidence-free.
/// </summary>
public sealed class RowEvidenceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private const string Secret = "rowsecret-7731";

    private const string EvidenceSpec = """
        schema = 1

        [section]
        id = "row-evidence"
        title = "Row evidence"

        [[row]]
        id = "evidence"
        title = "Evidence row"
        expected = "pass"
        depends_on = []

          [[row.step]]
          id = "read"
          tool = "stack_up"

          [[row.expect]]
          id = "rows-read"
          text = "rows read"
          source = "step"
          step = "read"
          pointer = "/result/rows/0"
          op = "exists"
          required = true

          [[row.expect]]
          id = "db-read"
          text = "db read"
          source = "db"
          database = "lab"
          sql = "select 1"
          op = "truthy"
          required = false

        [[row]]
        id = "expected-error"
        title = "Expected error"
        expected = "fails"
        depends_on = []

          [[row.step]]
          id = "tried"
          tool = "stack_up"
          expect_error = "Lab.Unavailable"

          [[row.expect]]
          id = "tried-read"
          text = "tried read"
          source = "step"
          step = "tried"
          pointer = "/result/rows/0"
          op = "exists"
          required = true
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-w3fb-evidence-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(Start);
    private readonly SecretVault _vault = new();
    private readonly AuditLog _audit;
    private readonly BundleWriter _writer;
    private readonly UatRunner _runner;

    public RowEvidenceTests()
    {
        string uatRuns = Path.Combine(_root, "uat-runs");
        _ = Directory.CreateDirectory(uatRuns);
        _ = _vault.Add(SecretKind.Password, Secret);
        var redactor = new Redactor(_vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        _audit = new AuditLog(LabPaths.FromRoot(_root), validator, redactor, _time);
        var leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
        var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new EvidenceTool() }), leases, _audit, redactor, _time);
        var evaluator = new ClauseEvaluator(new IClauseSource[] { new StepSource(), new FakeDbSource() }, redactor, _time);
        _runner = new UatRunner(dispatcher, leases, evaluator, new FakeProbe(), _time);
        _writer = new BundleWriter(uatRuns, validator, redactor, new SecretScanner(_vault), _time, "abcd1234", "linux", "build", null);
    }

    [Fact]
    public async Task RowFile_CarriesObservedAttemptsCheckedAtAndSteps()
    {
        await RunSpecAsync();

        using JsonDocument row = JsonDocument.Parse(await File.ReadAllTextAsync(RowPath("evidence")));
        JsonElement clause = row.RootElement.GetProperty("clauses")[0];
        Assert.Equal("rows-read", clause.GetProperty("id").GetString());
        Assert.Equal(1, clause.GetProperty("observed").GetProperty("a").GetInt32());
        Assert.Equal(1, clause.GetProperty("attempts").GetInt32());
        Assert.Equal(JsonValueKind.String, clause.GetProperty("checked_at").ValueKind);

        JsonElement steps = row.RootElement.GetProperty("steps");
        Assert.Equal(1, steps.GetArrayLength());
        JsonElement step = steps[0];
        Assert.Equal("stack_up", step.GetProperty("tool").GetString());
        Assert.True(step.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Number, step.GetProperty("duration_ms").ValueKind);
    }

    [Fact]
    public async Task ExpectErrorStep_ThatSucceeded_ShowsExpectedErrorAndOkTrue()
    {
        await RunSpecAsync();

        using JsonDocument row = JsonDocument.Parse(await File.ReadAllTextAsync(RowPath("expected-error")));
        JsonElement step = row.RootElement.GetProperty("steps")[0];
        Assert.Equal("Lab.Unavailable", step.GetProperty("expected_error").GetString());
        Assert.True(step.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task RunJson_CountsRedactions_WhenAClauseErrorHeldAVaultValue()
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);
        _writer.RunStarted("run-w3fb-redact", 9UL, loaded.Spec!);

        // The evaluator redacts its own errors before they reach the writer, so this clause error carries the raw value.
        _writer.RowFinished(new UatRowResult(
            "evidence",
            RowGrade.Fail,
            "clause failed",
            [new UatClauseResult("db-read", ClauseState.Unverified, "db refused " + Secret)]));
        _writer.RunFinished(new UatRunResult("run-w3fb-redact", 9UL, false, RunOverall.Incomplete, []));

        Assert.Null(_writer.WriteError);
        Assert.NotNull(_writer.Folder);
        string rowText = await File.ReadAllTextAsync(RowPath("evidence"));
        Assert.DoesNotContain(Secret, rowText, StringComparison.Ordinal);
        Assert.Contains("***", rowText, StringComparison.Ordinal);

        using JsonDocument run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "run.json")));
        Assert.True(run.RootElement.GetProperty("redactions").GetInt32() > 0);
        Assert.Equal("passed", run.RootElement.GetProperty("secret_scan").GetString());
    }

    [Fact]
    public async Task UatRunResultJson_HasNoObservedOrSteps()
    {
        UatRunResult run = await RunSpecAsync();

        string json = JsonSerializer.Serialize(run, WebOptions);

        Assert.DoesNotContain("\"observed\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"steps\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"attempts\"", json, StringComparison.Ordinal);
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

    private string RowPath(string rowId) => Path.Combine(_writer.Folder!, "rows", "row-evidence", rowId + ".json");

    private async Task<UatRunResult> RunSpecAsync()
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);
        Result<UatRunResult> run = await _runner.RunAsync(
            new UatRunRequest(loaded.Spec!, null, 7UL, null, null, PlanOnly: false), _writer, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Null(_writer.WriteError);
        Assert.NotNull(_writer.Folder);
        return run.Value!;
    }

    private sealed class FakeProbe : ICapabilityProbe
    {
        public Task<bool> IsAvailableAsync(string capability, CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>
    /// Answers every <c>db</c> clause with a failure that names the vault value, so the clause error holds a secret.
    /// </summary>
    private sealed class FakeDbSource : IClauseSource
    {
        public string Name => "db";

        public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct) =>
            Task.FromResult(Result<JsonElement>.Failure("db refused " + Secret));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EvidenceArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record EvidenceArgs(bool? Wipe = null);

    /// <summary>
    /// A step tool under the name <c>stack_up</c> that always succeeds with a two-row payload.
    /// </summary>
    private sealed class EvidenceTool : LabTool<EvidenceArgs>
    {
        public override string Name => "stack_up";

        public override string Description => "row evidence test stack_up";

        protected override Task<Result<object>> ExecuteAsync(EvidenceArgs args, LabCallContext context, CancellationToken ct) =>
            Task.FromResult(Result<object>.Success(new { rows = new[] { new { a = 1 } } }));
    }
}
