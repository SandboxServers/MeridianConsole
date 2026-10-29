using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Chaos;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Tomlyn;
using Tomlyn.Model;

namespace Dhadgar.Lab.Tests.Chaos;

/// <summary>
/// Chaos plan loading and validation (L29, R-BLAST): only C4 injection tools, only C10 proxies, and the shipped plan has no Postgres cut.
/// </summary>
public sealed class ChaosPlanTests
{
    private const string Valid = """
        schema = 1
        [plan]
        id = "unit-plan"
        workload = { spec = "docs/guides/uat-specs/lab-selftest.toml", rows = ["enroll-gateway"] }
        max_concurrent = 2
        quiet_s = [5, 30]
        [[fault]]
        tool = "fault_toxic_add"
        args = { proxy = "nodes-postgres", toxic = "latency", stream = "downstream", attributes = { latency = 1500 } }
        weight = 3
        duration_s = [5, 45]
        """;

    private readonly PathValidator _validator = new(NullLogger<PathValidator>.Instance);

    [Fact]
    public void Load_ValidPlan_Succeeds()
    {
        Result<ChaosPlan> plan = ChaosPlanLoader.Load(Valid);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.Equal("unit-plan", plan.Value!.Id);
        Assert.Equal(2, plan.Value.MaxConcurrent);
        Assert.Equal(5, plan.Value.MinQuietS);
        Assert.Equal(30, plan.Value.MaxQuietS);
        Assert.Single(plan.Value.Faults);
        Assert.Equal(3, plan.Value.Faults[0].Weight);
        Assert.Equal(45, plan.Value.Faults[0].MaxDurationS);
    }

    [Fact]
    public void Load_NonFaultTool_Refused()
    {
        string text = Valid.Replace("tool = \"fault_toxic_add\"", "tool = \"fault_heal\"", StringComparison.Ordinal);

        Result<ChaosPlan> plan = ChaosPlanLoader.Load(text);

        Assert.True(plan.IsFailure);
        Assert.Contains("C4 fault injection tool", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_LeasedJobTool_Refused()
    {
        string text = Valid.Replace("tool = \"fault_toxic_add\"", "tool = \"uat_run\"", StringComparison.Ordinal);

        Assert.True(ChaosPlanLoader.Load(text).IsFailure);
    }

    [Fact]
    public void Load_UnknownProxy_Refused()
    {
        string text = Valid.Replace("proxy = \"nodes-postgres\"", "proxy = \"made-up-proxy\"", StringComparison.Ordinal);

        Result<ChaosPlan> plan = ChaosPlanLoader.Load(text);

        Assert.True(plan.IsFailure);
        Assert.Contains("C10 proxy", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WeightOutOfRange_Refused()
    {
        string text = Valid.Replace("weight = 3", "weight = 0", StringComparison.Ordinal);

        Assert.True(ChaosPlanLoader.Load(text).IsFailure);
    }

    [Fact]
    public void Load_ZeroQuietMinimum_Refused()
    {
        // A zero gap would never advance the schedule clock, so it is refused.
        string text = Valid.Replace("quiet_s = [5, 30]", "quiet_s = [0, 30]", StringComparison.Ordinal);

        Assert.True(ChaosPlanLoader.Load(text).IsFailure);
    }

    [Fact]
    public void Load_NotToml_Refused()
    {
        Assert.True(ChaosPlanLoader.Load("schema = = 1").IsFailure);
    }

    [Fact]
    public void LoadFile_OutsideChaosFolder_Refused()
    {
        string root = Path.Combine(RepoRoot(), "docs", "guides", "uat-specs", "chaos");
        string outside = Path.Combine(RepoRoot(), "docs", "guides", "uat-specs", "lab-selftest.toml");

        Result<ChaosPlan> plan = ChaosPlanLoader.LoadFile(outside, root, _validator);

        Assert.True(plan.IsFailure);
        Assert.Contains("outside the chaos plan folder", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_NodesEnroll_Loads()
    {
        string root = Path.Combine(RepoRoot(), "docs", "guides", "uat-specs", "chaos");

        Result<ChaosPlan> plan = ChaosPlanLoader.LoadFile(Path.Combine(root, "nodes-enroll.toml"), root, _validator);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.Equal("nodes-enroll", plan.Value!.Id);
        Assert.Equal(new[] { "enroll-gateway" }, plan.Value.Workload.Rows);
    }

    [Fact]
    public void ShippedPlan_NoPostgresCut()
    {
        // D-ML71, F67: a Postgres cut gives the 500s that I1 never excuses. Latency only on nodes-postgres, and no disable or DB fault.
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "guides", "uat-specs", "chaos", "nodes-enroll.toml"));
        TomlTable root = TomlSerializer.Deserialize<TomlTable>(text)!;
        var faults = (TomlTableArray)root["fault"];

        foreach (TomlTable fault in faults)
        {
            string tool = (string)fault["tool"];
            Assert.NotEqual("fault_proxy_disable", tool);
            Assert.NotEqual("fault_db", tool);

            var args = (TomlTable)fault["args"];
            if (args.TryGetValue("proxy", out object? proxy) && (string)proxy == "nodes-postgres")
            {
                Assert.Equal("latency", (string)args["toxic"]);
            }
        }
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "docs", "guides", "uat-specs")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("the repository root was not found");
    }
}
