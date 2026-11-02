using System.Text.Json;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Processes;
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
    public async Task RowFile_TruncatesAnObservedValueOverTheEvidenceCap()
    {
        var redactor = new Redactor(_vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
        var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new EvidenceTool() }), leases, _audit, redactor, _time);
        JsonElement big = JsonSerializer.SerializeToElement(new { rows = new[] { new { a = new string('x', 20000) } } });
        var evaluator = new ClauseEvaluator(new IClauseSource[] { new StepSource(), new FakeDbSource(big) }, redactor, _time);
        var runner = new UatRunner(dispatcher, leases, evaluator, new FakeProbe(), _time);
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(loaded.Spec!, null, 7UL, null, null, PlanOnly: false), _writer, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.NotNull(_writer.Folder);
        using JsonDocument row = JsonDocument.Parse(await File.ReadAllTextAsync(RowPath("evidence")));
        JsonElement clause = row.RootElement.GetProperty("clauses").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == "db-read");
        JsonElement observed = clause.GetProperty("observed");
        Assert.True(observed.GetProperty("truncated").GetBoolean());
        Assert.Equal(16384, observed.GetProperty("text").GetString()!.Length);
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
    public async Task RunJson_ListsTheMembersOfPooledRows()
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);
        _writer.RunStarted("run-l30-members", 11UL, loaded.Spec!);
        _writer.RowFinished(new UatRowResult("evidence", RowGrade.Pass, "fine", [], Member: "lin-01"));
        _writer.RunFinished(new UatRunResult("run-l30-members", 11UL, false, RunOverall.Incomplete, []));

        using JsonDocument run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "run.json")));
        string[] members = run.RootElement.GetProperty("members").EnumerateArray().Select(m => m.GetString()!).ToArray();
        Assert.Equal(["lin-01"], members);
    }

    [Fact]
    public async Task RunJson_HasNoMembers_WhenNoRowWasPooled()
    {
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);
        _writer.RunStarted("run-l30-nomembers", 12UL, loaded.Spec!);
        _writer.RowFinished(new UatRowResult("evidence", RowGrade.Pass, "fine", []));
        _writer.RunFinished(new UatRunResult("run-l30-nomembers", 12UL, false, RunOverall.Incomplete, []));

        using JsonDocument run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "run.json")));
        Assert.Empty(run.RootElement.GetProperty("members").EnumerateArray());
    }

    [Fact]
    public void UnevaluatedSkip_IsCountedInRunJson_AndNamedInWarnings()
    {
        // I5 queues skipped at run end (D-ML80): the grade stays, but run.json says how many sources were not evaluated (N2).
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);
        _writer.RunStarted("run-n2-skip", 10UL, loaded.Spec!);
        _writer.RowFinished(new UatRowResult("evidence", RowGrade.Pass, "fine", []));
        var skip = new InvariantSkip("I5", "queues", "stack not up (no lab credentials)", "(run-end)");
        var invariants = new InvariantReport([], [], [skip]);
        _writer.RunFinished(new UatRunResult("run-n2-skip", 10UL, false, RunOverall.Incomplete, [], Invariants: invariants));

        Assert.Null(_writer.WriteError);
        Assert.NotNull(_writer.Folder);
        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(_writer.Folder!, "run.json")));
        Assert.Equal(1, run.RootElement.GetProperty("invariant_skips").GetInt32());
        string warnings = run.RootElement.GetProperty("warnings").GetRawText();
        Assert.Contains("I5", warnings, StringComparison.Ordinal);
        Assert.Contains("queues", warnings, StringComparison.Ordinal);
        Assert.Contains("(run-end)", warnings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UatRunResultJson_HasNoObservedOrSteps()
    {
        UatRunResult run = await RunSpecAsync();

        string json = JsonSerializer.Serialize(run, WebOptions);

        Assert.DoesNotContain("\"observed\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"steps\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"attempts\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("leaseOwner", json, StringComparison.Ordinal);
        Assert.DoesNotContain("startedAt", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bundle_UseBuild_FolderRunJsonAndLedgerCarryTheBuild()
    {
        var redactor = new Redactor(_vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var writer = new BundleWriter(Path.Combine(_root, "uat-runs"), validator, redactor, new SecretScanner(_vault), _time, "nobuild", "linux", string.Empty, null);
        writer.UseBuild(new BuildStamp("0123abcd", "0123abcd4567ef89", true, "."));

        await RunSpecAsync(writer);

        Assert.Contains("-0123abcd-", Path.GetFileName(writer.Folder!), StringComparison.Ordinal);
        using JsonDocument run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "run.json")));
        JsonElement build = run.RootElement.GetProperty("build");
        Assert.Equal("0123abcd4567ef89", build.GetProperty("commit").GetString());
        Assert.True(build.GetProperty("dirty").GetBoolean());
        string ledger = await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "ledger.md"));
        Assert.Contains("commit 0123abcd4567, dirty", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("commit not collected", run.RootElement.GetProperty("warnings").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunJson_MutantStamp_WritesMutant()
    {
        var redactor = new Redactor(_vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var writer = new BundleWriter(Path.Combine(_root, "uat-runs"), validator, redactor, new SecretScanner(_vault), _time, "mutant", "linux", string.Empty, null);
        writer.UseBuild(new BuildStamp("mutant-m1-0123abcd", "0123abcd4567ef89", true, string.Empty, "m1"));

        await RunSpecAsync(writer);

        using JsonDocument run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "run.json")));
        JsonElement build = run.RootElement.GetProperty("build");
        Assert.Equal("mutant-m1-0123abcd", build.GetProperty("id").GetString());
        Assert.Equal("m1", build.GetProperty("mutant").GetString());
        Assert.True(build.GetProperty("dirty").GetBoolean());
    }

    [Fact]
    public async Task RunJson_PlainStamp_HasNoMutantKey()
    {
        var redactor = new Redactor(_vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var writer = new BundleWriter(Path.Combine(_root, "uat-runs"), validator, redactor, new SecretScanner(_vault), _time, "plain", "linux", string.Empty, null);
        writer.UseBuild(new BuildStamp("0123abcd", "0123abcd4567ef89", false, "."));

        await RunSpecAsync(writer);

        using JsonDocument run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "run.json")));
        JsonElement build = run.RootElement.GetProperty("build");
        Assert.False(build.TryGetProperty("mutant", out _));
    }

    [Fact]
    public async Task Runner_EventsAndBundle_ShareOneFolder()
    {
        var redactor = new Redactor(_vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        LabPaths paths = LabPaths.FromRoot(_root);
        using var observations = new LabObservationSink(redactor, _time, validator, [paths.UatRuns]);
        var leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
        var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new EvidenceTool() }), leases, _audit, redactor, _time);
        var evaluator = new ClauseEvaluator(new IClauseSource[] { new StepSource(), new FakeDbSource() }, redactor, _time);
        var runner = new UatRunner(dispatcher, leases, evaluator, new FakeProbe(), _time, observations, paths);
        var writer = new BundleWriter(paths.UatRuns, validator, redactor, new SecretScanner(_vault), _time, "abcd1234", "linux", "build", null);
        writer.UseBuild(new BuildStamp("abcd1234", string.Empty, false, string.Empty));
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(loaded.Spec!, null, 7UL, null, null, PlanOnly: false), writer, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.NotNull(writer.Folder);
        Assert.Contains("-abcd1234-", Path.GetFileName(writer.Folder!), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(writer.Folder!, "events.jsonl")));
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
    public async Task RowJson_HasStartedEndedAndMember()
    {
        await RunSpecAsync();

        using JsonDocument row = JsonDocument.Parse(await File.ReadAllTextAsync(RowPath("evidence")));
        DateTimeOffset started = row.RootElement.GetProperty("started_at").GetDateTimeOffset();
        DateTimeOffset ended = row.RootElement.GetProperty("ended_at").GetDateTimeOffset();
        Assert.True(started <= ended);
        Assert.Equal(JsonValueKind.Null, row.RootElement.GetProperty("member").ValueKind);
        Assert.False(row.RootElement.TryGetProperty("at", out _));
    }

    [Fact]
    public async Task ClauseJson_HasTextOpAndValue()
    {
        const string valueSpec = """
            schema = 1

            [section]
            id = "row-value"
            title = "Row value"

            [[row]]
            id = "valued"
            title = "Valued row"
            expected = "pass"
            depends_on = []

              [[row.step]]
              id = "read"
              tool = "stack_up"

              [[row.expect]]
              id = "state-ready"
              text = "state is ready"
              source = "step"
              step = "read"
              pointer = "/result/state"
              op = "eq"
              value = "ready"
              required = true
            """;
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-value.toml", valueSpec);
        Assert.NotNull(loaded.Spec);

        Result<UatRunResult> run = await _runner.RunAsync(
            new UatRunRequest(loaded.Spec!, null, 7UL, null, null, PlanOnly: false), _writer, CancellationToken.None);

        Assert.True(run.IsSuccess, run.Error);
        using JsonDocument row = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "rows", "row-value", "valued.json")));
        JsonElement clause = row.RootElement.GetProperty("clauses")[0];
        Assert.Equal("state is ready", clause.GetProperty("text").GetString());
        Assert.Equal("eq", clause.GetProperty("op").GetString());
        Assert.Equal("ready", clause.GetProperty("value").GetString());
    }

    [Fact]
    public async Task RunJson_ToolsAreTheDistinctStepTools()
    {
        await RunSpecAsync();

        using JsonDocument run = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_writer.Folder!, "run.json")));
        string[] tools = run.RootElement.GetProperty("tools").EnumerateArray().Select(tool => tool.GetString()!).ToArray();
        Assert.Equal(new[] { "stack_up" }, tools);
    }

    [Fact]
    public async Task LeaseId_OfTheRun_IsInNoBundleFile_AndLeaseOwnerIsRecorded()
    {
        var redactor = new Redactor(_vault);
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var leases = new LeaseBook(_time, new NullLabEvents(), new LeaseOptions());
        LeaseGrant grant = leases.Acquire("owner-l16f", "uat", null, false, null).Value!;
        var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new EvidenceTool() }), leases, _audit, redactor, _time);
        var evaluator = new ClauseEvaluator(new IClauseSource[] { new StepSource(), new FakeDbSource() }, redactor, _time);
        var runner = new UatRunner(dispatcher, leases, evaluator, new FakeProbe(), _time);
        var writer = new BundleWriter(Path.Combine(_root, "uat-runs"), validator, redactor, new SecretScanner(_vault), _time, "abcd1234", "linux", "build", null);
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(loaded.Spec!, null, 7UL, grant.LeaseId, null, PlanOnly: false), writer, CancellationToken.None);

        Assert.True(run.IsSuccess, run.Error);
        Assert.NotNull(writer.Folder);
        foreach (string file in Directory.EnumerateFiles(writer.Folder!, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(grant.LeaseId, await File.ReadAllTextAsync(file), StringComparison.Ordinal);
        }

        using JsonDocument runJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "run.json")));
        Assert.Equal("owner-l16f", runJson.RootElement.GetProperty("lease_owner").GetString());
    }

    [Fact]
    public async Task LogSlice_VaultValueInRingLine_IsAbsent()
    {
        var line = new LogLine(Start, "Information", "Dhadgar.Test", null, "service said " + Secret);
        _writer.UseLogs(new FakeLogSource(new RunLogSlice("tc-log", [line])));

        await RunSpecAsync();

        string logFile = Path.Combine(_writer.Folder!, "logs", "tc-log.jsonl");
        Assert.True(File.Exists(logFile));
        Assert.DoesNotContain(Secret, await File.ReadAllTextAsync(logFile));
    }

    [Fact]
    public void LogSlice_LineOutsideTheRunWindow_IsAbsent()
    {
        var before = new LogLine(Start.AddSeconds(-1), "Information", "c", null, "before");
        var inside = new LogLine(Start.AddSeconds(1), "Information", "c", null, "inside");
        var after = new LogLine(Start.AddSeconds(60), "Information", "c", null, "after");

        IReadOnlyList<LogLine> kept = SupervisorRunLogSource.LinesInWindow([before, inside, after], Start, Start.AddSeconds(30));

        Assert.Equal(new[] { "inside" }, kept.Select(line => line.Message));
    }

    [Fact]
    public async Task Repro_LabelWithNewline_RoundTripsThroughTomlyn()
    {
        const string label = "a\nb\t\"c\"";
        var validator = new PathValidator(NullLogger<PathValidator>.Instance);
        var writer = new BundleWriter(Path.Combine(_root, "uat-runs"), validator, new Redactor(_vault), new SecretScanner(_vault), _time,
            "abcd1234", "linux", "build", label);

        await RunSpecAsync(writer);

        string text = await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "repro", "repro.toml"));
        Dictionary<string, object?> model = Tomlyn.TomlSerializer.Deserialize<Dictionary<string, object?>>(text)!;
        Assert.Equal(label, (string?)model["label"]);
    }

    [Fact]
    public async Task RowEvidence_ExceptionAndFiveHundred_InRowWindow_AreWritten()
    {
        const string ExceptionText = "System.InvalidOperationException: boom\n"
            + "   at Dhadgar.Nodes.Enroll.HandleAsync(Object o) in /src/Enroll.cs:line 42\n"
            + "   at System.Threading.Tasks.Task.Run()";
        _writer.UseLogs(new FakeLogSource(new RunLogSlice("nodes", [
            new LogLine(Start, "Error", "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", null, ExceptionText),
            new LogLine(Start, "Error", "Dhadgar.ServiceDefaults.GlobalExceptionHandler", null, "Unhandled exception. TraceId: t1"),
            new LogLine(Start, "Information", "Microsoft.AspNetCore.Hosting.Diagnostics", null,
                "Request finished HTTP/1.1 POST http://nodes:5000/api/v1/agents/enroll?x=1 - 500 - text/plain 10.0ms"),
            new LogLine(Start, "Information", "Microsoft.AspNetCore.Hosting.Diagnostics", null,
                "Request finished HTTP/1.1 GET http://nodes:5000/api/v1/agents/ca-certificate - 200 - application/json 1.0ms"),
            new LogLine(Start.AddSeconds(-5), "Information", "Microsoft.AspNetCore.Hosting.Diagnostics", null,
                "Request finished HTTP/1.1 POST http://nodes:5000/api/v1/agents/enroll - 500 - text/plain 1.0ms"),
        ])));

        await RunSpecAsync();

        using JsonDocument row = JsonDocument.Parse(await File.ReadAllTextAsync(RowPath("evidence")));
        JsonElement evidence = row.RootElement.GetProperty("evidence");
        JsonElement exceptions = evidence.GetProperty("exceptions");
        Assert.Equal(1, exceptions.GetArrayLength());
        Assert.Equal("nodes", exceptions[0].GetProperty("service").GetString());
        Assert.Equal("System.InvalidOperationException", exceptions[0].GetProperty("type").GetString());
        Assert.Equal(1, exceptions[0].GetProperty("frames").GetArrayLength());
        Assert.Contains("Dhadgar.Nodes.Enroll.HandleAsync", exceptions[0].GetProperty("frames")[0].GetString(), StringComparison.Ordinal);

        JsonElement http = evidence.GetProperty("http");
        Assert.Equal(1, http.GetArrayLength());
        Assert.Equal(500, http[0].GetProperty("status").GetInt32());
        Assert.Equal("POST", http[0].GetProperty("method").GetString());
        Assert.Equal("/api/v1/agents/enroll", http[0].GetProperty("route").GetString());
        Assert.Equal(0, http[0].GetProperty("faults").GetArrayLength());
    }

    [Fact]
    public async Task RowEvidence_PlantedSecretInExceptionMessage_IsAbsentFromRowFile()
    {
        string exceptionText = "System.InvalidOperationException: connect failed with " + Secret + "\n"
            + "   at Dhadgar.Nodes.Enroll.HandleAsync(Object o) in /src/Enroll.cs:line 42";
        _writer.UseLogs(new FakeLogSource(new RunLogSlice("nodes", [
            new LogLine(Start, "Error", "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", null, exceptionText),
            new LogLine(Start, "Error", "Dhadgar.ServiceDefaults.GlobalExceptionHandler", null, "Unhandled exception. TraceId: t2"),
            new LogLine(Start, "Information", "Microsoft.AspNetCore.Hosting.Diagnostics", null,
                "Request finished HTTP/1.1 POST http://nodes:5000/api/v1/agents/enroll?token=" + Secret + " - 500 - text/plain 10.0ms"),
        ])));

        await RunSpecAsync();

        string rowText = await File.ReadAllTextAsync(RowPath("evidence"));
        Assert.DoesNotContain(Secret, rowText, StringComparison.Ordinal);
        Assert.Contains("***", rowText, StringComparison.Ordinal);
        using JsonDocument row = JsonDocument.Parse(rowText);
        JsonElement exceptions = row.RootElement.GetProperty("evidence").GetProperty("exceptions");
        Assert.Equal(1, exceptions.GetArrayLength());
        Assert.Equal("System.InvalidOperationException", exceptions[0].GetProperty("type").GetString());
    }

    private string RowPath(string rowId) =>Path.Combine(_writer.Folder!, "rows", "row-evidence", rowId + ".json");

    private async Task<UatRunResult> RunSpecAsync(BundleWriter? sink = null)
    {
        BundleWriter writer = sink ?? _writer;
        SpecLoadOutcome loaded = SpecLoader.LoadText("row-evidence.toml", EvidenceSpec);
        Assert.NotNull(loaded.Spec);
        Result<UatRunResult> run = await _runner.RunAsync(
            new UatRunRequest(loaded.Spec!, null, 7UL, null, null, PlanOnly: false), writer, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Null(writer.WriteError);
        Assert.NotNull(writer.Folder);
        return run.Value!;
    }

    private sealed class FakeProbe : ICapabilityProbe
    {
        public Task<bool> IsAvailableAsync(string capability, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FakeLogSource(params RunLogSlice[] slices) : IRunLogSource
    {
        public IReadOnlyList<RunLogSlice> Slices(DateTimeOffset from, DateTimeOffset to) => slices;
    }

    /// <summary>
    /// Answers every <c>db</c> clause with a failure that names the vault value, so the clause error holds a secret.
    /// </summary>
    private sealed class FakeDbSource : IClauseSource
    {
        private readonly JsonElement? _payload;

        public FakeDbSource(JsonElement? payload = null)
        {
            _payload = payload;
        }

        public string Name => "db";

        public Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct) =>
            Task.FromResult(_payload is { } payload
                ? Result<JsonElement>.Success(payload)
                : Result<JsonElement>.Failure("db refused " + Secret));
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
