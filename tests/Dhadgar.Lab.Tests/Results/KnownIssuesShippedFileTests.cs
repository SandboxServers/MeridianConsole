using Dhadgar.Lab.Results;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// The shipped <c>known-issues.toml</c> loads with no loader error, carries the L26 seeds, and every seed scopes its
/// patterns to a service the kind can name (D-ML94).
/// </summary>
public sealed class KnownIssuesShippedFileTests
{
    private static readonly string[] Seeds =
    [
        // D-ML96: KI-DV13, KI-DV12, KI-DV3, KI-DELAYED-PLUGIN and KI-94 are documentation only and no longer ship.
        "KI-NOTIF-OUTBOX", "KI-RABBIT-HOST", "KI-DISCORD-SECRETS-PORT",
    ];

    [Fact]
    public void ShippedFile_LoadsWithoutLoaderErrors()
    {
        Result<KnownIssues> shipped = KnownIssues.Load(ShippedPath());

        Assert.True(shipped.IsSuccess, shipped.Error);
    }

    [Fact]
    public void ShippedFile_CarriesEverySeed()
    {
        KnownIssues shipped = KnownIssues.Load(ShippedPath()).Value!;
        var ids = shipped.Issues.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        foreach (string seed in Seeds)
        {
            Assert.Contains(seed, ids);
        }
    }

    [Fact]
    public void ShippedFile_ServicesScopedIssuesUseOnlyNamedKinds()
    {
        KnownIssues shipped = KnownIssues.Load(ShippedPath()).Value!;

        foreach (KnownIssue issue in shipped.Issues.Where(i => i.Scope.Services.Count > 0))
        {
            foreach (KnownIssuePattern pattern in issue.Patterns)
            {
                Assert.Contains(pattern.Kind, new[] { "exc", "http", "inv" });
            }
        }
    }

    private static string ShippedPath() => Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory),
        "docs", "guides", "uat-specs", "known-issues.toml");
}
