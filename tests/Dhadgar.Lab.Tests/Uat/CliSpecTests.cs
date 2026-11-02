using Dhadgar.Lab.Uat.Spec;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat;

/// <summary>
/// The shipped <c>cli.toml</c> (L37) loads and keeps its five rows and its Linux container pool requirement. No Docker.
/// </summary>
public sealed class CliSpecTests
{
    [Fact]
    public void CliToml_HasFiveRowsAndLinuxContainerPool()
    {
        string path = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs", "cli.toml");
        var outcome = SpecLoader.LoadText("cli.toml", File.ReadAllText(path));

        Assert.True(outcome.Problems.Count == 0, string.Join("; ", outcome.Problems.Select(p => $"line {p.Line} {p.Message}")));
        UatSpec spec = Assert.IsType<UatSpec>(outcome.Spec);
        Assert.Equal("cli", spec.Section.Id);
        Assert.Equal(
            new[] { "version", "auth-login", "nodes-list", "notifications-admin", "gateway-down" },
            spec.Rows.Select(r => r.Id).ToArray());

        var pool = Assert.IsAssignableFrom<IReadOnlyDictionary<string, System.Text.Json.JsonElement>>(spec.Section.RequiresPool);
        Assert.Equal("linux", pool["os"].GetString());
        Assert.Equal("container", pool["kind"].GetString());
    }
}
