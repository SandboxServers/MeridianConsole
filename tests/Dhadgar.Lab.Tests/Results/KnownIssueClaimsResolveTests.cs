using System.Text.RegularExpressions;
using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// Every <c>known_issue = "..."</c> claim under <c>docs/guides/uat-specs/</c> names an id in the shipped known-issues file.
/// </summary>
public sealed class KnownIssueClaimsResolveTests
{
    private static readonly Regex Claim = new(@"known_issue\s*=\s*""([^""]+)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [Fact]
    public void EveryKnownIssueClaim_NamesAShippedIssue()
    {
        string repoRoot = LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory);
        string specs = Path.Combine(repoRoot, "docs", "guides", "uat-specs");
        Assert.True(Directory.Exists(specs), specs);

        Result<KnownIssues> shipped = KnownIssues.Load(Path.Combine(specs, "known-issues.toml"));
        Assert.True(shipped.IsSuccess, shipped.Error);
        var ids = shipped.Value.Issues.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        var unresolved = new List<string>();
        foreach (string file in Directory.EnumerateFiles(specs, "*.toml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (Match match in Claim.Matches(text))
            {
                if (!ids.Contains(match.Groups[1].Value))
                {
                    unresolved.Add(Path.GetRelativePath(repoRoot, file) + ": " + match.Groups[1].Value);
                }
            }
        }

        Assert.Empty(unresolved);
    }

    [Fact]
    public void ShippedFile_LoadsWithNoProblems()
    {
        Result<KnownIssues> shipped = KnownIssues.Load(ShippedPath());

        Assert.True(shipped.IsSuccess, shipped.Error);
        Assert.NotEmpty(shipped.Value.Issues);
    }

    [Fact]
    public void Match_KiF67_NodesI1OutsideClaimingRow_IsNew()
    {
        Result<KnownIssues> shipped = KnownIssues.Load(ShippedPath());
        Assert.True(shipped.IsSuccess, shipped.Error);
        // The requires evidence of KI-F67 (L26 B5) is part of the claiming row; the outside row is new by scope either way.
        FailureSignature[] signatures =
        [
            new FailureSignature("inv", "inv:I1|nodes|unhandled", "nodes", "I1"),
            new FailureSignature("clause", "clause:catalog-nodes/nodes-pg-down-enroll/step:enroll-down|NotHeld", null, null),
        ];

        KnownMatch outside = shipped.Value.Match(new RowFailure("catalog-other", "any-row", null, signatures));
        KnownMatch inside = shipped.Value.Match(new RowFailure("catalog-nodes", "nodes-pg-down-enroll", null, signatures));

        Assert.Equal("new", outside.State);
        Assert.Equal("known", inside.State);
        Assert.Equal("KI-F67", inside.IssueId);
    }

    private static string ShippedPath() => Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory),
        "docs", "guides", "uat-specs", "known-issues.toml");
}
