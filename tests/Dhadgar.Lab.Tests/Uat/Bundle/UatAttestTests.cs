using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Equal(before, File.ReadAllText(rowPath));
    }
}
