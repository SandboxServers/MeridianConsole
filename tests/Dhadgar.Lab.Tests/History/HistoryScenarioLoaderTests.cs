using Dhadgar.Lab.History;
using Dhadgar.Lab.Uat.Spec;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// The enrollment scenario loader (C19): every shipped scenario loads, and unknown or malformed keys are refused with a line.
/// </summary>
public sealed class HistoryScenarioLoaderTests
{
    private const string Valid = """
        schema = 1

        [[scenario]]
        id = "enroll-race"
        clients = 4
        retry = "none"
        first_op_action = "hold_request"
        """;

    [Fact]
    public void ShippedEnrollHistory_EveryScenarioLoads()
    {
        string file = Path.Combine(
            LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs", "catalog", "enroll-history.toml");

        var outcome = HistoryScenarioLoader.LoadText("enroll-history.toml", File.ReadAllText(file));

        Assert.Empty(outcome.Problems);
        Assert.Equal(
            new[] { "enroll-race", "enroll-race-pg-latency", "enroll-lost-response", "enroll-lost-response-new-key" },
            outcome.Scenarios.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void ShippedEnrollHistory_LatencyScenarioCarriesItsFault()
    {
        string file = Path.Combine(
            LabIsolationTests.FindRepoRoot(AppContext.BaseDirectory), "docs", "guides", "uat-specs", "catalog", "enroll-history.toml");

        var outcome = HistoryScenarioLoader.LoadText("enroll-history.toml", File.ReadAllText(file));

        HistoryScenario latency = Assert.Single(outcome.Scenarios, s => s.Id == "enroll-race-pg-latency");
        UatFault fault = Assert.Single(latency.Faults);
        Assert.Equal("fault_toxic_add", fault.Tool);
        Assert.Equal("before_steps", fault.Start);
    }

    [Fact]
    public void Load_Valid_ReadsTheFields()
    {
        var outcome = HistoryScenarioLoader.LoadText("t.toml", Valid);

        Assert.Empty(outcome.Problems);
        HistoryScenario scenario = Assert.Single(outcome.Scenarios);
        Assert.Equal(4, scenario.Clients);
        Assert.Equal(HistoryRetry.None, scenario.Retry);
        Assert.Equal(HistoryFirstOpAction.HoldRequest, scenario.FirstOpAction);
    }

    [Fact]
    public void Load_UnknownScenarioKey_Refused()
    {
        string text = Valid.Replace("retry = \"none\"", "retry = \"none\"\nsurprise = true", StringComparison.Ordinal);

        var outcome = HistoryScenarioLoader.LoadText("t.toml", text);

        Assert.Empty(outcome.Scenarios);
        UatProblem problem = Assert.Single(outcome.Problems);
        Assert.Contains("unknown key surprise", problem.Message, StringComparison.Ordinal);
        Assert.True(problem.Line > 1);
    }

    [Fact]
    public void Load_UnknownRootKey_Refused()
    {
        var outcome = HistoryScenarioLoader.LoadText("t.toml", "extra = 1\n" + Valid);

        Assert.Empty(outcome.Scenarios);
        Assert.Contains(outcome.Problems, p => p.Message.Contains("unknown key extra", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_ClientsOutOfRange_Refused()
    {
        string text = Valid.Replace("clients = 4", "clients = 9", StringComparison.Ordinal);

        var outcome = HistoryScenarioLoader.LoadText("t.toml", text);

        Assert.Empty(outcome.Scenarios);
        Assert.Contains(outcome.Problems, p => p.Message.Contains("clients must be 1 to 8", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_UnknownRetry_Refused()
    {
        string text = Valid.Replace("retry = \"none\"", "retry = \"always\"", StringComparison.Ordinal);

        var outcome = HistoryScenarioLoader.LoadText("t.toml", text);

        Assert.Empty(outcome.Scenarios);
        Assert.Contains(outcome.Problems, p => p.Message.Contains("retry must be", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_DuplicateId_Refused()
    {
        string text = Valid + "\n" + Valid[Valid.IndexOf("[[scenario]]", StringComparison.Ordinal)..];

        var outcome = HistoryScenarioLoader.LoadText("t.toml", text);

        Assert.Empty(outcome.Scenarios);
        Assert.Contains(outcome.Problems, p => p.Message.Contains("duplicate scenario id", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_InvalidToml_ReturnsProblemsWithoutThrowing()
    {
        var outcome = HistoryScenarioLoader.LoadText("t.toml", "schema = = 1");

        Assert.Empty(outcome.Scenarios);
        Assert.NotEmpty(outcome.Problems);
    }
}
