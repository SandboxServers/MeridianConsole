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
}
