using System.Text.Json;
using Dhadgar.Lab.Services;
using Dhadgar.Lab.Tools.Cli;
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

    private static UatSpec LoadSpec()
    {
        string path = Path.Combine(LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs", "cli.toml");
        var outcome = SpecLoader.LoadText("cli.toml", File.ReadAllText(path));
        Assert.True(outcome.Problems.Count == 0, string.Join("; ", outcome.Problems.Select(p => $"line {p.Line} {p.Message}")));
        return Assert.IsType<UatSpec>(outcome.Spec);
    }

    private static string[] ArgsOf(UatStep step) =>
        step.Args is { } args && args.TryGetProperty("args", out JsonElement list)
            ? list.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray()
            : [];

    private static IEnumerable<(UatRow Row, UatStep Step, string[] Args)> CliRunSteps(UatRow row) =>
        row.Steps.Select(step => (row, step, ArgsOf(step)))
            .Where(t => string.Equals(t.step.Tool, "cli_run", StringComparison.Ordinal));

    private static bool IsVerb(string[] args, string sub) => args.Length >= 2 && args[0] == "auth" && args[1] == sub;

    [Fact]
    public void EveryCliRunStep_PassesTheAllowlist()
    {
        foreach (var (row, step, args) in LoadSpec().Rows.SelectMany(CliRunSteps))
        {
            var check = CliAllowlist.Check(args);
            Assert.True(check.IsSuccess, $"row {row.Id} step {step.Id}: {check.Error}");
        }
    }

    [Fact]
    public void EveryLoginStep_UsesThePinnedDevClient()
    {
        int logins = 0;
        foreach (var (row, step, args) in LoadSpec().Rows.SelectMany(CliRunSteps).Where(t => IsVerb(t.Args, "login")))
        {
            logins++;
            int id = Array.IndexOf(args, "--client-id");
            int secret = Array.IndexOf(args, "--client-secret");
            Assert.True(id >= 0 && id + 1 < args.Length && args[id + 1] == IdentityLabAdapter.DevClientId, $"row {row.Id} step {step.Id}");
            Assert.True(secret >= 0 && secret + 1 < args.Length && args[secret + 1] == IdentityLabAdapter.DevClientSecret, $"row {row.Id} step {step.Id}");
        }

        Assert.True(logins > 0);
    }

    [Fact]
    public void EveryLoginRow_LogsOutFirst()
    {
        foreach (UatRow row in LoadSpec().Rows)
        {
            var steps = CliRunSteps(row).ToList();
            int firstLogin = steps.FindIndex(t => IsVerb(t.Args, "login"));
            if (firstLogin < 0)
            {
                continue;
            }

            Assert.True(steps.Take(firstLogin).Any(t => IsVerb(t.Args, "logout")), $"row {row.Id} logs in without an earlier auth logout");
        }
    }
}
