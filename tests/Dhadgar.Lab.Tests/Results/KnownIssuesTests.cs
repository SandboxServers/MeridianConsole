using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// Known-issue matching (D-ML46): a claim is never a pass, every signature must match one open issue within its scope, and a
/// fixed issue that matches is a regression. The loader lists every problem.
/// </summary>
public sealed class KnownIssuesTests
{
    private const string Open = "http:500|-|POST|/api/v1/agents/enroll|none";
    private const string Other = "http:500|-|POST|/api/v1/agents/other|none";

    [Fact]
    public void Match_ClaimedIssueSignatureDiffers_IsNew()
    {
        KnownIssues issues = Load("""
            schema = 1
            [[issue]]
            id = "KI-A"
            signatures = ["http:500|-|POST|/api/v1/agents/enroll|none"]
            state = "open"
            """);

        KnownMatch match = issues.Match(new RowFailure("nodes", "R1", "KI-A", [Sig(Other)]));

        Assert.Equal("claimed_mismatch", match.State);
        Assert.Contains("claimed but signature", match.Reason);
    }

    [Fact]
    public void Match_OneOfTwoSignaturesUnmatched_IsNew()
    {
        KnownIssues issues = Load("""
            schema = 1
            [[issue]]
            id = "KI-A"
            signatures = ["http:500|-|POST|/api/v1/agents/enroll|none"]
            state = "open"
            """);

        KnownMatch claimed = issues.Match(new RowFailure("nodes", "R1", "KI-A", [Sig(Open), Sig(Other)]));
        KnownMatch unclaimed = issues.Match(new RowFailure("nodes", "R1", null, [Sig(Open), Sig(Other)]));

        Assert.Equal("claimed_mismatch", claimed.State);
        Assert.Equal("new", unclaimed.State);
        Assert.Null(unclaimed.IssueId);
    }

    [Fact]
    public void Match_OutOfScopeService_IsNew()
    {
        KnownIssues issues = Load("""
            schema = 1
            [[issue]]
            id = "KI-A"
            signatures = ["http:500|-|POST|/api/v1/agents/enroll|none"]
            state = "open"
            [issue.scope]
            services = ["nodes"]
            """);

        KnownMatch match = issues.Match(new RowFailure("nodes", "R1", null, [new FailureSignature("http", Open, "gateway", null)]));

        Assert.Equal("new", match.State);
        Assert.Null(match.IssueId);
    }

    [Fact]
    public void Match_FixedIssue_IsRegressed()
    {
        KnownIssues issues = Load("""
            schema = 1
            [[issue]]
            id = "KI-A"
            signatures = ["http:500|-|POST|/api/v1/agents/enroll|none"]
            state = "fixed"
            """);

        KnownMatch match = issues.Match(new RowFailure("nodes", "R1", null, [Sig(Open)]));

        Assert.Equal("regressed", match.State);
        Assert.Equal("KI-A", match.IssueId);
        Assert.Equal("regressed:KI-A", match.Reason);
    }

    [Fact]
    public void Match_AllSignaturesSameIssue_IsKnown()
    {
        KnownIssues issues = Load("""
            schema = 1
            [[issue]]
            id = "KI-A"
            patterns = ['^http:5\d\d\|[^|]*\|POST\|/api/v1/agents/']
            state = "open"
            """);

        KnownMatch match = issues.Match(new RowFailure("nodes", "R1", "KI-A", [Sig(Open), Sig(Other)]));

        Assert.Equal("known", match.State);
        Assert.Equal("KI-A", match.IssueId);
    }

    [Fact]
    public void Load_UnanchoredPattern_IsInvalid()
    {
        Result<KnownIssues> result = KnownIssues.LoadText("test.toml", """
            schema = 1
            [[issue]]
            id = "KI-A"
            patterns = ['http:5\d\d']
            state = "open"
            """);

        Assert.True(result.IsFailure);
        Assert.Contains("must start with ^", result.Error);
    }

    [Fact]
    public void Load_DotStarPattern_IsInvalid()
    {
        Result<KnownIssues> result = KnownIssues.LoadText("test.toml", """
            schema = 1
            [[issue]]
            id = "KI-A"
            patterns = ['^http:.*']
            state = "open"
            """);

        Assert.True(result.IsFailure);
        Assert.Contains("too broad", result.Error);
    }

    [Fact]
    public void Load_NoSignaturesNoPatterns_IsInvalid()
    {
        Result<KnownIssues> result = KnownIssues.LoadText("test.toml", """
            schema = 1
            [[issue]]
            id = "KI-A"
            state = "open"
            """);

        Assert.True(result.IsFailure);
        Assert.Contains("needs signatures or patterns", result.Error);
    }

    [Fact]
    public void Load_ListsEveryProblem()
    {
        Result<KnownIssues> result = KnownIssues.LoadText("test.toml", """
            schema = 1
            [[issue]]
            id = "KI-A"
            signatures = ["http:500|-|POST|/a|none"]
            state = "open"
            [[issue]]
            id = "KI-A"
            signatures = ["http:500|-|POST|/b|none"]
            state = "open"
            [[issue]]
            id = "KI-B"
            signatures = ["nonsense"]
            state = "maybe"
            """);

        Assert.True(result.IsFailure);
        Assert.Contains("duplicate id", result.Error);
        Assert.Contains("state must be open or fixed", result.Error);
        Assert.Contains("no known kind prefix", result.Error);
    }

    [Fact]
    public void Load_AbsentFile_IsEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), "ki-absent-" + Path.GetRandomFileName() + ".toml");

        Result<KnownIssues> result = KnownIssues.Load(path);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value.Issues);
    }

    [Fact]
    public void Shipped_F67_MatchesEnrollFiveHundredAndI1()
    {
        KnownIssues issues = LoadShipped();
        var row = new RowFailure("nodes", "R1", null,
        [
            new FailureSignature("http", Open, "nodes", null),
            new FailureSignature("inv", "inv:I1|nodes|orphan", "nodes", "I1"),
        ]);

        KnownMatch match = issues.Match(row);

        Assert.Equal("known", match.State);
        Assert.Equal("KI-F67", match.IssueId);
    }

    [Fact]
    public void Shipped_F69_DoesNotMatchAnyFiveHundred()
    {
        KnownIssues issues = LoadShipped();

        KnownMatch match = issues.Match(new RowFailure("nodes", "R1", "KI-F69", [new FailureSignature("http", Open, "nodes", null)]));

        Assert.Equal("claimed_mismatch", match.State);
        Assert.Equal("KI-F69", match.IssueId);
    }

    [Fact]
    public void Shipped_EveryIssueIsOpenAndLoads()
    {
        KnownIssues issues = LoadShipped();

        Assert.Contains(issues.Issues, i => i.Id == "KI-ENROLL-RETRY");
        Assert.Contains(issues.Issues, i => i.Id == "KI-F67" && i.State == "open");
        Assert.Contains(issues.Issues, i => i.Id == "KI-F69" && i.State == "open");
    }

    private static FailureSignature Sig(string text) => new("http", text, "nodes", null);

    private static KnownIssues Load(string toml)
    {
        Result<KnownIssues> result = KnownIssues.LoadText("test.toml", toml);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static KnownIssues LoadShipped()
    {
        string path = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory),
            "docs", "guides", "uat-specs", "known-issues.toml");
        Result<KnownIssues> result = KnownIssues.Load(path);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }
}
