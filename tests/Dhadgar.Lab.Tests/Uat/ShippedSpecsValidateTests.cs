using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
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

    /// <summary>
    /// Classifies the shipped files by path (F65): specs are <c>uat-specs/*.toml</c> and <c>uat-specs/catalog/*.toml</c>,
    /// invariant files are <c>uat-specs/invariants/*.toml</c>. <c>known-issues.toml</c>, <c>enroll-history.toml</c> and
    /// <c>soak.toml</c> are skipped by name.
    /// </summary>
    [Fact]
    public void ShippedSpecs_ByPath_EverySpecAndInvariantFileValidates()
    {
        string specsDir = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs");
        IReadOnlyList<string> specs = ShippedSpecFiles(specsDir);
        Assert.NotEmpty(specs);

        foreach (string relative in specs)
        {
            var outcome = SpecLoader.LoadText(relative, File.ReadAllText(Path.Combine(specsDir, relative)));
            Assert.True(outcome.Problems.Count == 0, relative + ": " + string.Join("; ", outcome.Problems.Select(p => $"line {p.Line} {p.Message}")));
            Assert.NotNull(outcome.Spec);
        }

        string invariantDir = Path.Combine(specsDir, "invariants");
        if (!Directory.Exists(invariantDir))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(invariantDir, "*.toml", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            if (name == "soak.toml")
            {
                continue;
            }

            Result<IReadOnlyList<DbCheck>> parsed = I3DbChecksInvariant.ParseChecks(name, File.ReadAllText(file));
            Assert.False(parsed.IsFailure, name + ": " + parsed.Error);
        }
    }

    /// <summary>
    /// The catalog folder is part of the scan, so a return to <c>TopDirectoryOnly</c> for <c>catalog/</c> fails here.
    /// </summary>
    [Fact]
    public void ShippedSpecs_CatalogFolder_IsIncluded()
    {
        string specsDir = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs");
        IReadOnlyList<string> specs = ShippedSpecFiles(specsDir);

        Assert.Contains("catalog/gateway.toml", specs);
    }

    /// <summary>
    /// The spec files under <paramref name="specsDir"/> as forward-slash relative paths: top-level files except
    /// <c>known-issues.toml</c>, plus <c>catalog/</c> files except <c>enroll-history.toml</c>.
    /// </summary>
    private static List<string> ShippedSpecFiles(string specsDir)
    {
        var result = new List<string>();
        foreach (string file in Directory.GetFiles(specsDir, "*.toml", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(file) != "known-issues.toml")
            {
                result.Add(Path.GetFileName(file));
            }
        }

        string catalogDir = Path.Combine(specsDir, "catalog");
        if (Directory.Exists(catalogDir))
        {
            foreach (string file in Directory.GetFiles(catalogDir, "*.toml", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(file) != "enroll-history.toml")
                {
                    result.Add("catalog/" + Path.GetFileName(file));
                }
            }
        }

        return result;
    }
}
