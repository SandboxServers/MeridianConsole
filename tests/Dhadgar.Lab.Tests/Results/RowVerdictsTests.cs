using System.Text.Json.Nodes;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// The one row-verdict path (L26 B1, B3, B5): step pseudo-signatures for unmet expected errors, and the shipped catalog claims
/// that resolve from row evidence.
/// </summary>
public sealed class RowVerdictsTests
{
    [Fact]
    public void Step_ExpectedErrorNotMet_IsSignature()
    {
        // An expected http:400 answered 200: the step is NotHeld, and the shipped KI-F69 (row evidence) does not excuse it.
        ParsedRow row = Parse("""
            {"section":"catalog-nodes","id":"nodes-enroll-malformed","grade":"FAIL","reason":"step post",
             "steps":[{"id":"post","ok":true,"expect_error":"http:400","expect_met":false}]}
            """);

        RowVerdict verdict = RowVerdicts.Of(row, ShippedIssues(), static text => text);

        Assert.Contains(verdict.Signatures, s => s.Text == "clause:catalog-nodes/nodes-enroll-malformed/step:post|NotHeld");
        Assert.Equal("new", verdict.Match!.State);
        Assert.Null(verdict.Match.IssueId);
    }

    [Fact]
    public void Step_ExpectedErrorMet_GivesNoSignature()
    {
        ParsedRow row = Parse("""
            {"section":"catalog-nodes","id":"nodes-enroll-malformed","grade":"FAIL",
             "steps":[{"id":"post","ok":false,"expect_error":"http:400","expect_met":true}],
             "clauses":[{"id":"problem-json","state":"NotHeld"}]}
            """);

        RowVerdict verdict = RowVerdicts.Of(row, KnownIssues.Empty, static text => text);

        Assert.DoesNotContain(verdict.Signatures, s => s.Text.Contains("step:post", StringComparison.Ordinal));
        Assert.Single(verdict.Signatures);
    }

    [Fact]
    public void Step_ErroredAndStoppedRow_IsSignature()
    {
        ParsedRow row = Parse("""
            {"section":"catalog-nodes","id":"nodes-pg-down-enroll","grade":"BLOCKED",
             "steps":[{"id":"mint","ok":false}]}
            """);

        RowVerdict verdict = RowVerdicts.Of(row, KnownIssues.Empty, static text => text);

        Assert.Contains(verdict.Signatures, s => s.Text == "clause:catalog-nodes/nodes-pg-down-enroll/step:mint|NotHeld");
    }

    [Fact]
    public void CatalogClaims_ResolveFromRowEvidence()
    {
        KnownIssues issues = ShippedIssues();

        // The pg row as the catalog writes it: four Unverified clauses, the enroll-down step NotHeld, and the I1 violation.
        ParsedRow pg = Parse(PgRow(includeStep: true, includeViolation: true));
        KnownMatch pgMatch = RowVerdicts.Of(pg, issues, static text => text).Match!;
        Assert.Equal("known", pgMatch.State);
        Assert.Equal("KI-F67", pgMatch.IssueId);

        // The same row without the requires evidence (no step NotHeld, no I1 violation) is a claimed mismatch.
        ParsedRow pgWithout = Parse(PgRow(includeStep: false, includeViolation: false));
        KnownMatch withoutMatch = RowVerdicts.Of(pgWithout, issues, static text => text).Match!;
        Assert.Equal("claimed_mismatch", withoutMatch.State);
        Assert.Contains("required evidence", withoutMatch.Reason, StringComparison.Ordinal);

        // The F69 row: two NotHeld problem-json clauses, claimed as KI-F69.
        ParsedRow f69 = Parse("""
            {"section":"catalog-nodes","id":"nodes-enroll-malformed","grade":"FAIL","known_issue":"KI-F69",
             "clauses":[{"id":"problem-json","state":"NotHeld"},{"id":"problem-json-empty","state":"NotHeld"}]}
            """);
        KnownMatch f69Match = RowVerdicts.Of(f69, issues, static text => text).Match!;
        Assert.Equal("known", f69Match.State);
        Assert.Equal("KI-F69", f69Match.IssueId);
    }

    [Fact]
    public void Shipped_F69Http_MatchesTheHttpCaseOnly()
    {
        KnownIssues issues = ShippedIssues();
        var http = new FailureSignature("http", "http:400|-|POST|/api/v1/agents/enroll|none", "nodes", null);

        KnownMatch match = issues.Match(new RowFailure("catalog-nodes", "nodes-enroll-malformed", null, [http]));

        Assert.Equal("known", match.State);
        Assert.Equal("KI-F69-HTTP", match.IssueId);
    }

    private static string PgRow(bool includeStep, bool includeViolation)
    {
        string steps = includeStep ? "[{\"id\":\"enroll-down\",\"ok\":false,\"expect_error\":\"http:503\",\"expect_met\":false}]" : "[]";
        string violations = includeViolation
            ? "[{\"invariant_id\":\"I1\",\"service\":\"nodes\",\"check_id\":\"unhandled\",\"allowed\":false}]"
            : "[]";
        return "{\"section\":\"catalog-nodes\",\"id\":\"nodes-pg-down-enroll\",\"grade\":\"BLOCKED\",\"known_issue\":\"KI-F67\","
            + "\"clauses\":[{\"id\":\"down-503\",\"state\":\"Unverified\"},{\"id\":\"after-201\",\"state\":\"Unverified\"},"
            + "{\"id\":\"recovered\",\"state\":\"Unverified\"},{\"id\":\"token-used\",\"state\":\"Unverified\"}],"
            + "\"steps\":" + steps + ",\"violations\":" + violations + "}";
    }

    private static ParsedRow Parse(string json)
    {
        Result<ParsedRow> parsed = RowVerdicts.Parse(JsonNode.Parse(json)!.AsObject());
        Assert.True(parsed.IsSuccess, parsed.Error);
        return parsed.Value;
    }

    private static KnownIssues ShippedIssues()
    {
        string path = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory),
            "docs", "guides", "uat-specs", "known-issues.toml");
        Result<KnownIssues> result = KnownIssues.Load(path);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }
}
