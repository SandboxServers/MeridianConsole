using System.Text.Json;
using System.Text.Json.Nodes;
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
        var tool = new UatAttestTool(LabPaths.FromRoot(lab), _validator, redactor, time, leases);
        JsonElement args = JsonSerializer.SerializeToElement(new { run_id = RunId, section = Section, row = Row, clause_id = "human-look", verdict = "held" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("call-1", grant.LeaseId, "mcp-test"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        string row = File.ReadAllText(Path.Combine(bundle, "rows", Section, Row + ".json"));
        Assert.Contains("\"lease_owner\": \"owner-b\"", row);
        Assert.Contains("\"caller\": \"mcp-test\"", row);
        string ledger = File.ReadAllText(Path.Combine(bundle, "ledger.md"));
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
        string before = File.ReadAllText(rowPath);

        var time = new FakeTimeProvider(At);
        var leases = new LeaseBook(time, new NullLabEvents(), new LeaseOptions());
        using var vault = new SecretVault();
        var tool = new UatAttestTool(LabPaths.FromRoot(lab), _validator, new Redactor(vault), time, leases);
        JsonElement args = JsonSerializer.SerializeToElement(new { run_id = RunId, section = Section, row = Row, clause_id = "human-look", verdict = "held" });

        Result<JsonElement> result = await tool.InvokeAsync(args, new LabCallContext("call-2", "no-such-lease", "mcp-test"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("[Lab.", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(rowPath));
    }

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
}
