using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Audit;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Bundle;

public sealed class UatAttestTests : IDisposable
{
    private const string RunId = "attest1";
    private const string Section = "plan-check";
    private const string Row = "key-held";
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-lab-l16-attest-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    public UatAttestTests()
    {
        _folder = Path.Combine(_root, "20260101-nobuild-" + RunId);
        string rows = Path.Combine(_folder, "rows", Section);
        _ = Directory.CreateDirectory(rows);
        File.WriteAllText(Path.Combine(_folder, "run.json"), """
            {
              "run_id": "attest1",
              "os": "linux",
              "secret_scan": "passed",
              "overall": "fail",
              "summary": {}
            }
            """);
        File.WriteAllText(Path.Combine(rows, Row + ".json"), """
            {
              "section": "plan-check",
              "id": "key-held",
              "title": "Operator confirms the key",
              "expected": "present",
              "grade": "NEEDS_HUMAN",
              "reason": "a human clause is pending",
              "at": "2026-01-01T00:00:00+00:00",
              "clauses": [
                { "id": "human-look", "pointer": "", "state": "Pending", "source": "human", "required": true },
                { "id": "auto-check", "pointer": "/ok", "state": "Held", "source": "step", "required": true }
              ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static readonly AttestedBy Attester = new("owner-a", "mcp");

    /// <summary>
    /// A row whose only step is an <c>expect_error</c> step that succeeds, so the row grades FAIL, plus one human clause.
    /// </summary>
    private const string HiddenExpectSpec = """
        schema = 1

        [section]
        id = "attest-fail"
        title = "Attest fail fixture"

        [[row]]
        id = "key-held"
        title = "Operator confirms the key"
        expected = "present"
        depends_on = []

          [[row.step]]
          id = "probe"
          tool = "lab_status"
          args = {}
          expect_error = "Lab.Unavailable"

          [[row.expect]]
          id = "human-look"
          text = "Operator confirms the key"
          source = "human"
          required = true
        """;

    [Fact]
    public async Task UatRun_ReturnedOverallEqualsRunJson()
    {
        string root = Path.Combine(Path.GetTempPath(), "meridian-lab-l25-overall-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var vault = new SecretVault();
            var redactor = new Redactor(vault);
            var time = new FakeTimeProvider(At);
            using var audit = new AuditLog(LabPaths.FromRoot(root), _validator, redactor, time);
            var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
            var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new AcceptingStub() }), leases, audit, redactor, time);
            var runner = new UatRunner(dispatcher, leases, new ClauseEvaluator(Array.Empty<IClauseSource>(), redactor, time), new ProbeYes(), time);
            UatSpec spec = SpecLoader.LoadText("overall-equals.toml", HiddenExpectSpec).Spec!;
            var writer = new BundleWriter(Path.Combine(root, "uat-runs"), _validator, redactor, new SecretScanner(vault), time,
                "nobuild", "test-system", "test-build", null);

            Result<UatRunResult> run = await runner.RunAsync(
                new UatRunRequest(spec, null, 1UL, null, null, PlanOnly: false), writer, CancellationToken.None);

            Assert.True(run.IsSuccess);
            Assert.NotNull(writer.Folder);
            Assert.Equal(RunOverall.Fail, run.Value!.Overall);
            JsonNode? runJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "run.json"), CancellationToken.None));
            Assert.Equal(runJson?["overall"]?.GetValue<string>(), writer.FinalOverall);
            Assert.Equal("fail", writer.FinalOverall);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task Attest_FailRowWithHiddenExpectFailure_StaysFail()
    {
        string root = Path.Combine(Path.GetTempPath(), "meridian-lab-l16-fail-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var vault = new SecretVault();
            var redactor = new Redactor(vault);
            var time = new FakeTimeProvider(At);
            using var audit = new AuditLog(LabPaths.FromRoot(root), _validator, redactor, time);
            var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
            var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new AcceptingStub() }), leases, audit, redactor, time);
            var runner = new UatRunner(dispatcher, leases, new ClauseEvaluator(Array.Empty<IClauseSource>(), redactor, time), new ProbeYes(), time);
            UatSpec spec = SpecLoader.LoadText("attest-fail.toml", HiddenExpectSpec).Spec!;
            var writer = new BundleWriter(Path.Combine(root, "uat-runs"), _validator, redactor, new SecretScanner(vault), time,
                "nobuild", "test-system", "test-build", null);

            Result<UatRunResult> run = await runner.RunAsync(
                new UatRunRequest(spec, null, 1UL, null, null, PlanOnly: false), writer, CancellationToken.None);

            Assert.True(run.IsSuccess);
            Assert.Equal(RowGrade.Fail, run.Value!.Rows[0].Grade);
            Assert.NotNull(writer.Folder);

            Result<string> result = BundleAttestor.Attest(writer.Folder!, run.Value.RunId, "attest-fail", "key-held", "human-look",
                "held", null, At, Attester, redactor, _validator);

            Assert.True(result.IsFailure);
            Assert.Contains("NEEDS_HUMAN or UNVERIFIED", result.Error, StringComparison.Ordinal);
            JsonNode? runJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "run.json"), CancellationToken.None));
            Assert.Equal("fail", runJson?["overall"]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// A row with a plain step and one required human clause, so the row grades NEEDS_HUMAN and an attest can hold it.
    /// </summary>
    private const string BuildLineSpec = """
        schema = 1

        [section]
        id = "attest-build"
        title = "Attest build fixture"

        [[row]]
        id = "key-build"
        title = "Operator confirms the build"
        expected = "present"
        depends_on = []

          [[row.step]]
          id = "probe"
          tool = "lab_status"
          args = {}

          [[row.expect]]
          id = "human-look"
          text = "Operator confirms the build"
          source = "human"
          required = true
        """;

    [Fact]
    public async Task Attest_LedgerKeepsTheBuildLine()
    {
        string root = Path.Combine(Path.GetTempPath(), "meridian-lab-l16-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var vault = new SecretVault();
            var redactor = new Redactor(vault);
            var time = new FakeTimeProvider(At);
            using var audit = new AuditLog(LabPaths.FromRoot(root), _validator, redactor, time);
            var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
            var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new AcceptingStub() }), leases, audit, redactor, time);
            var runner = new UatRunner(dispatcher, leases, new ClauseEvaluator(Array.Empty<IClauseSource>(), redactor, time), new ProbeYes(), time);
            UatSpec spec = SpecLoader.LoadText("attest-build.toml", BuildLineSpec).Spec!;
            var writer = new BundleWriter(Path.Combine(root, "uat-runs"), _validator, redactor, new SecretScanner(vault), time,
                "nobuild", "test-system", string.Empty, null);
            writer.UseBuild(new BuildStamp("0123abcd", "0123abcd4567ef89", true, "."));

            Result<UatRunResult> run = await runner.RunAsync(
                new UatRunRequest(spec, null, 1UL, null, null, PlanOnly: false), writer, CancellationToken.None);

            Assert.True(run.IsSuccess, run.Error);
            Assert.NotNull(writer.Folder);
            Assert.Contains("-0123abcd-", Path.GetFileName(writer.Folder!), StringComparison.Ordinal);

            Result<string> result = BundleAttestor.Attest(writer.Folder!, run.Value!.RunId, "attest-build", "key-build", "human-look",
                "held", null, At, Attester, redactor, _validator);

            Assert.True(result.IsSuccess, result.Error);
            string ledger = await File.ReadAllTextAsync(Path.Combine(writer.Folder!, "ledger.md"), CancellationToken.None);
            Assert.Contains("0123abcd (commit 0123abcd4567, dirty)", ledger, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NoArgs is the LabTool<TArgs> argument type; the tool base binds it from JSON.")]
    private sealed record NoArgs(bool? Unused = null);

    /// <summary>
    /// A lab tool that always succeeds, so the step's <c>expect_error</c> is hidden. It takes the name of an
    /// Open-gated entry in <c>ToolPolicy</c>, because the dispatcher refuses any name the table does not classify.
    /// </summary>
    private sealed class AcceptingStub : LabTool<NoArgs>
    {
        public override string Name => "lab_status";

        public override string Description => "test lab_status";

        protected override Task<Result<object>> ExecuteAsync(NoArgs args, LabCallContext context, CancellationToken ct) =>
            Task.FromResult(Result<object>.Success(new { ok = true }));
    }

    private sealed class ProbeYes : ICapabilityProbe
    {
        public Task<bool> IsAvailableAsync(string capability, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public void Attest_HumanClauseHeld_GradesRowPass()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "held",
            "checked by hand", At, Attester, redactor, _validator);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("PASS", result.Value);
        string row = File.ReadAllText(Path.Combine(_folder, "rows", Section, Row + ".json"));
        Assert.Contains("\"grade\": \"PASS\"", row);
        Assert.Contains("\"state\": \"Held\"", row);
        Assert.Contains("\"clause_id\": \"human-look\"", row);
        Assert.Contains("\"verdict\": \"held\"", row);
        string ledger = File.ReadAllText(Path.Combine(_folder, "ledger.md"));
        Assert.Contains("PASS", ledger);
    }

    [Fact]
    public void Attest_NonHumanClause_IsRefused()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        string rowPath = Path.Combine(_folder, "rows", Section, Row + ".json");
        string before = File.ReadAllText(rowPath);

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "auto-check", "held",
            null, At, Attester, redactor, _validator);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
        Assert.Equal(before, File.ReadAllText(rowPath));
    }

    [Fact]
    public void Attest_PassVocabulary_IsRefused()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "pass",
            null, At, Attester, redactor, _validator);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
    }

    [Fact]
    public void Attest_OldBundleWithAt_StillRenders()
    {
        // The fixture row carries only the old "at" field (no started_at or ended_at), as bundles written before L16f do.
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "held",
            null, At, Attester, redactor, _validator);

        Assert.True(result.IsSuccess, result.Error);
        string ledger = File.ReadAllText(Path.Combine(_folder, "ledger.md"));
        Assert.Contains("PASS", ledger);
    }

    [Fact]
    public void Attest_ReadRowsScrub_RedactsTitleBeforeRender()
    {
        // The renderer turns a tab into a space, so only a scrub before the render removes the vault value.
        using var vault = new SecretVault();
        _ = vault.Add(SecretKind.Password, "tab\tsecret-l16f");
        var redactor = new Redactor(vault);
        string rowPath = Path.Combine(_folder, "rows", Section, Row + ".json");
        File.WriteAllText(rowPath, File.ReadAllText(rowPath).Replace("\"Operator confirms the key\"", "\"tab\\tsecret-l16f\"", StringComparison.Ordinal));

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "held",
            null, At, Attester, redactor, _validator);

        Assert.True(result.IsSuccess, result.Error);
        string ledger = File.ReadAllText(Path.Combine(_folder, "ledger.md"));
        Assert.DoesNotContain("tab\tsecret-l16f", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("tab secret-l16f", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void Attest_RecordsLeaseOwnerAndCaller()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "held",
            null, At, Attester, redactor, _validator);

        Assert.True(result.IsSuccess, result.Error);
        string row = File.ReadAllText(Path.Combine(_folder, "rows", Section, Row + ".json"));
        Assert.Contains("\"lease_owner\": \"owner-a\"", row);
        Assert.Contains("\"caller\": \"mcp\"", row);
        string ledger = File.ReadAllText(Path.Combine(_folder, "ledger.md"));
        Assert.Contains("- Attested: human-look held by owner-a via mcp", ledger);
    }

    [Fact]
    public void Attest_NoteWithPasswordSegment_WritesNothing()
    {
        string rowPath = Path.Combine(_folder, "rows", Section, Row + ".json");
        string ledgerPath = Path.Combine(_folder, "ledger.md");
        File.WriteAllText(ledgerPath, "# ledger before\n");
        string runPath = Path.Combine(_folder, "run.json");
        string[] before = [File.ReadAllText(rowPath), File.ReadAllText(ledgerPath), File.ReadAllText(runPath)];

        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "held", "Password=hunter2x",
            At, Attester, redactor, _validator, new SecretScanner(vault));

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.Unavailable]", result.Error, StringComparison.Ordinal);
        string[] after = [File.ReadAllText(rowPath), File.ReadAllText(ledgerPath), File.ReadAllText(runPath)];
        Assert.Equal(before, after);
    }

    [Fact]
    public void Attest_SetsReproFidelityToEvidenceOnly()
    {
        string reproPath = Path.Combine(_folder, "repro", "repro.toml");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(reproPath)!);
        File.WriteAllText(reproPath, "seed = 1\nfidelity = \"exact\"\n");

        using var vault = new SecretVault();
        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "held", null,
            At, Attester, new Redactor(vault), _validator, new SecretScanner(vault));

        Assert.True(result.IsSuccess, result.Error);
        string repro = File.ReadAllText(reproPath);
        Assert.Contains("fidelity = \"evidence_only\"", repro, StringComparison.Ordinal);
        Assert.DoesNotContain("fidelity = \"exact\"", repro, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UatAttestTool_RecordsTheLeaseOwnerAndTheCallContextCaller()
    {
        string lab = Path.Combine(_root, "lab");
        _ = Directory.CreateDirectory(Path.Combine(lab, "uat-runs"));
        string bundle = Path.Combine(lab, "uat-runs", Path.GetFileName(_folder));
        Directory.Move(_folder, bundle);

        var time = new FakeTimeProvider(At);
        var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
        LeaseGrant grant = leases.Acquire("owner-b", "uat", null, false, null).Value;
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using var results = new ResultsService(LabPaths.FromRoot(lab), _validator, redactor, new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance, time);
        var tool = new UatAttestTool(LabPaths.FromRoot(lab), _validator, redactor, time, leases, vault, results);
        JsonElement args = JsonSerializer.SerializeToElement(new { run_id = RunId, section = Section, row = Row, clause_id = "human-look", verdict = "held" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("call-1", grant.LeaseId, "mcp-test"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        string row = await File.ReadAllTextAsync(Path.Combine(bundle, "rows", Section, Row + ".json"));
        Assert.Contains("\"lease_owner\": \"owner-b\"", row);
        Assert.Contains("\"caller\": \"mcp-test\"", row);
        string ledger = await File.ReadAllTextAsync(Path.Combine(bundle, "ledger.md"));
        Assert.Contains("- Attested: human-look held by owner-b via mcp-test", ledger);
    }

    [Fact]
    public async Task UatAttestTool_UnknownLease_IsRefusedAndTheRowIsUnchanged()
    {
        string lab = Path.Combine(_root, "lab");
        _ = Directory.CreateDirectory(Path.Combine(lab, "uat-runs"));
        string bundle = Path.Combine(lab, "uat-runs", Path.GetFileName(_folder));
        Directory.Move(_folder, bundle);
        string rowPath = Path.Combine(bundle, "rows", Section, Row + ".json");
        string before = await File.ReadAllTextAsync(rowPath);

        var time = new FakeTimeProvider(At);
        var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using var results = new ResultsService(LabPaths.FromRoot(lab), _validator, redactor, new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance, time);
        var tool = new UatAttestTool(LabPaths.FromRoot(lab), _validator, redactor, time, leases, vault, results);
        JsonElement args = JsonSerializer.SerializeToElement(new { run_id = RunId, section = Section, row = Row, clause_id = "human-look", verdict = "held" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("call-2", "no-such-lease", "mcp-test"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("[Lab.", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(rowPath));
    }

    [Fact]
    public async Task Writer_KnownIssuesUnchecked_KnownFailuresNull()
    {
        string root = Path.Combine(Path.GetTempPath(), "meridian-lab-l25-writer-" + Guid.NewGuid().ToString("N"));
        try
        {
            (string folder, _) = await WriteRunBundleAsync(root, HiddenExpectSpec, "writer-unchecked.toml");

            JsonNode? runJson = await ReadRunJsonAsync(folder);
            Assert.Null(runJson?["known_failures"]);
            Assert.False(runJson?["known_issues"]?["checked"]?.GetValue<bool>());
            Assert.Equal(KnownIssueCheck.BundleUncheckedReason, runJson?["known_issues"]?["reason"]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Writer_KnownIssueMatched_RunJsonAndRowRecordTheKnownFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "meridian-lab-l26-writer-known-" + Guid.NewGuid().ToString("N"));
        try
        {
            Result<KnownIssues> issues = KnownIssues.LoadText("known-issues.toml", """
                schema = 1

                [[issue]]
                id = "KI-L26-1"
                signatures = ["clause:attest-fail/key-held/step:probe|NotHeld"]
                state = "open"
                """);
            Assert.True(issues.IsSuccess, issues.Error);
            (string folder, _) = await WriteRunBundleAsync(root, HiddenExpectSpec, "writer-known.toml",
                KnownIssueCheck.Of(issues.Value!, new string('0', 64)));

            JsonNode? runJson = await ReadRunJsonAsync(folder);
            Assert.Equal("pass_with_known", runJson?["overall"]?.GetValue<string>());
            Assert.Empty(runJson!["new_failures"]!.AsArray());
            Assert.Equal("attest-fail/key-held", runJson?["known_failures"]?[0]?.GetValue<string>());
            Assert.True(runJson?["known_issues"]?["checked"]?.GetValue<bool>());

            JsonNode? row = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "rows", "attest-fail", "key-held.json"), CancellationToken.None));
            Assert.Equal("KI-L26-1", row?["known_match"]?["issue_id"]?.GetValue<string>());
            Assert.Equal("known", row?["known_match"]?["state"]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Attest_KnownIssuesUnchecked_KnownFailuresNull()
    {
        string root = Path.Combine(Path.GetTempPath(), "meridian-lab-l25-attest-unchecked-" + Guid.NewGuid().ToString("N"));
        try
        {
            (string folder, string runId) = await WriteRunBundleAsync(root, BuildLineSpec, "attest-unchecked.toml");
            using var vault = new SecretVault();
            var redactor = new Redactor(vault);

            Result<string> result = BundleAttestor.Attest(folder, runId, "attest-build", "key-build", "human-look",
                "held", null, At, Attester, redactor, _validator);

            Assert.True(result.IsSuccess, result.Error);
            JsonNode? runJson = await ReadRunJsonAsync(folder);
            Assert.Null(runJson?["known_failures"]);
            Assert.False(runJson?["known_issues"]?["checked"]?.GetValue<bool>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Attest_ReingestsBundle_DbRowGradeUpdated()
    {
        string lab = Path.Combine(_root, "lab");
        _ = Directory.CreateDirectory(Path.Combine(lab, "uat-runs"));
        string bundle = Path.Combine(lab, "uat-runs", Path.GetFileName(_folder));
        Directory.Move(_folder, bundle);
        var time = new FakeTimeProvider(At);
        var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
        LeaseGrant grant = leases.Acquire("owner-b", "uat", null, false, null).Value;
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using var results = new ResultsService(LabPaths.FromRoot(lab), _validator, redactor, new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance, time);
        Assert.True((await results.IngestAsync(bundle, CancellationToken.None)).IsSuccess);
        var tool = new UatAttestTool(LabPaths.FromRoot(lab), _validator, redactor, time, leases, vault, results);
        JsonElement args = JsonSerializer.SerializeToElement(new { run_id = RunId, section = Section, row = Row, clause_id = "human-look", verdict = "held" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("call-3", grant.LeaseId, "mcp-test"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value.GetProperty("ingest").GetProperty("ok").GetBoolean());
        Result<IReadOnlyList<RowGradeOf>> grades = await results.ReadAsync(db => db.GradesOf(Section, Row, 0), CancellationToken.None);
        Assert.True(grades.IsSuccess, grades.Error);
        Assert.Equal("PASS", Assert.Single(grades.Value).Grade);
    }

    [Fact]
    public async Task Attest_IngestFails_AttestStillCommitted()
    {
        string lab = Path.Combine(_root, "lab");
        _ = Directory.CreateDirectory(Path.Combine(lab, "uat-runs"));
        await File.WriteAllTextAsync(Path.Combine(lab, "results"), "a file where the results folder belongs", CancellationToken.None);
        string bundle = Path.Combine(lab, "uat-runs", Path.GetFileName(_folder));
        Directory.Move(_folder, bundle);
        var time = new FakeTimeProvider(At);
        var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
        LeaseGrant grant = leases.Acquire("owner-b", "uat", null, false, null).Value;
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        using var results = new ResultsService(LabPaths.FromRoot(lab), _validator, redactor, new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance, time);
        var tool = new UatAttestTool(LabPaths.FromRoot(lab), _validator, redactor, time, leases, vault, results);
        JsonElement args = JsonSerializer.SerializeToElement(new { run_id = RunId, section = Section, row = Row, clause_id = "human-look", verdict = "held" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("call-4", grant.LeaseId, "mcp-test"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        string row = await File.ReadAllTextAsync(Path.Combine(bundle, "rows", Section, Row + ".json"));
        Assert.Contains("\"lease_owner\": \"owner-b\"", row);
        JsonElement ingest = result.Value.GetProperty("ingest");
        Assert.False(ingest.GetProperty("ok").GetBoolean());
        Assert.Contains("[Lab.Unavailable]", ingest.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    /// <summary>Runs one spec through the runner into a bundle under <paramref name="root"/> and returns the folder and run id.</summary>
    private async Task<(string Folder, string RunId)> WriteRunBundleAsync(string root, string specText, string specName,
        KnownIssueCheck? known = null)
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        var time = new FakeTimeProvider(At);
        using var audit = new AuditLog(LabPaths.FromRoot(root), _validator, redactor, time);
        var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
        var dispatcher = new LabToolDispatcher(new LabToolRegistry(new ILabTool[] { new AcceptingStub() }), leases, audit, redactor, time);
        var runner = new UatRunner(dispatcher, leases, new ClauseEvaluator(Array.Empty<IClauseSource>(), redactor, time), new ProbeYes(), time);
        UatSpec spec = SpecLoader.LoadText(specName, specText).Spec!;
        var writer = new BundleWriter(Path.Combine(root, "uat-runs"), _validator, redactor, new SecretScanner(vault), time,
            "nobuild", "test-system", "test-build", null);

        if (known is not null)
        {
            writer.UseKnownIssues(known);
        }

        Result<UatRunResult> run = await runner.RunAsync(
            new UatRunRequest(spec, null, 1UL, null, null, PlanOnly: false), writer, CancellationToken.None);

        Assert.True(run.IsSuccess, run.Error);
        Assert.NotNull(writer.Folder);
        return (writer.Folder!, run.Value!.RunId);
    }

    private static async Task<JsonNode?> ReadRunJsonAsync(string folder) =>
        JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "run.json"), CancellationToken.None));

    [Fact]
    public void Attest_SetsFidelityEvidenceOnly()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "not_held",
            null, At, Attester, redactor, _validator);

        Assert.True(result.IsSuccess, result.Error);
        string run = File.ReadAllText(Path.Combine(_folder, "run.json"));
        Assert.Contains("\"fidelity\": \"evidence_only\"", run);
    }

    [Fact]
    public void Attest_SkippedRow_IsRefused()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        string rowPath = Path.Combine(_folder, "rows", Section, Row + ".json");
        File.WriteAllText(rowPath, File.ReadAllText(rowPath).Replace("\"grade\": \"NEEDS_HUMAN\"", "\"grade\": \"SKIPPED\"", StringComparison.Ordinal));
        string before = File.ReadAllText(rowPath);

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "held",
            null, At, Attester, redactor, _validator);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
        Assert.Equal(before, File.ReadAllText(rowPath));
    }

    public static TheoryData<string?, string> BadNoteOrClauseId => new()
    {
        { new string('a', 1001), "human-look" },
        { "a\nb", "human-look" },
        { "ok", "../x" },
        { "ok", "human-look\n" },
    };

    [Theory]
    [MemberData(nameof(BadNoteOrClauseId))]
    public void Attest_LongNote_ControlCharNote_BadClauseId_AreRefused(string? note, string clauseId)
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        string rowPath = Path.Combine(_folder, "rows", Section, Row + ".json");
        string before = File.ReadAllText(rowPath);

        Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, clauseId, "held",
            note, At, Attester, redactor, _validator);

        Assert.True(result.IsFailure);
        Assert.Contains("[Lab.InvalidArgument]", result.Error);
        if (clauseId.EndsWith('\n'))
        {
            // A trailing newline must fail the id pattern itself, not a later lookup.
            Assert.Contains("clause id is not valid", result.Error, StringComparison.Ordinal);
        }
        Assert.Equal(before, File.ReadAllText(rowPath));
    }

    [Fact]
    public void Attest_NoteWithFormatSeparatorOrTagCharacter_IsRefused()
    {
        using var vault = new SecretVault();
        var redactor = new Redactor(vault);
        string rowPath = Path.Combine(_folder, "rows", Section, Row + ".json");
        string before = File.ReadAllText(rowPath);
        string[] notes =
        [
            "ok" + char.ConvertFromUtf32(0xE0041) + "dleh",
            "ok" + "\u202E" + " dleh",
            "a" + "\u2028" + "b",
            "a" + "\u2029" + "b",
        ];

        foreach (string note in notes)
        {
            Result<string> result = BundleAttestor.Attest(_folder, RunId, Section, Row, "human-look", "held",
                note, At, Attester, redactor, _validator);

            Assert.True(result.IsFailure);
            Assert.Contains("[Lab.InvalidArgument]", result.Error);
        }

        Assert.Equal(before, File.ReadAllText(rowPath));
    }
}
