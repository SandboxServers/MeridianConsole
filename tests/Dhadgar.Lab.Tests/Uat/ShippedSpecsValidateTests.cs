using Dhadgar.Lab.Uat.Spec;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// Every spec shipped under <c>docs/guides/uat-specs</c> parses and validates, so a typo in a shipped spec fails CI.
/// </summary>
public sealed class ShippedSpecsValidateTests
{
    [Fact]
    public void EveryShippedSpec_ParsesWithoutProblems()
    {
        var specsDir = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs");
        // known-issues.toml is not a flow spec (C12); the work-packets spec for this test skips it by name.
        var files = Directory.GetFiles(specsDir, "*.toml", SearchOption.TopDirectoryOnly)
            .Where(f => !string.Equals(Path.GetFileName(f), "known-issues.toml", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var outcome = SpecLoader.LoadText(Path.GetFileName(file), File.ReadAllText(file));
            Assert.True(outcome.Problems.Count == 0, Path.GetFileName(file) + ": " + string.Join("; ", outcome.Problems.Select(p => $"line {p.Line} {p.Message}")));
            Assert.NotNull(outcome.Spec);
        }
    }
}
