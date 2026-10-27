using System.Text.Json;
using Dhadgar.Agent.Windows.Uat.Infra;
using Dhadgar.Agent.Windows.Uat.Preflight;
using Dhadgar.Agent.Windows.Uat.Results;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class AgentRunTreeTests
{
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, null, false)]
    public void AgentRunTree_Rule_PassesOnlyForUserOrCleanAgent(bool launchedByAgent, bool? treeClean, bool expectedPass)
    {
        var check = PreflightChecks.AgentRunTree(launchedByAgent, treeClean);

        Assert.Equal("agentRunTree", check.Name);
        Assert.Equal(expectedPass, check.Passed);
        Assert.False(check.RequiredForStackOnly);
        Assert.True(check.RequiredForRun);
    }

    [Fact]
    public void GitWorkTree_StatusArguments_ExcludeOnlyDocsAndClaude()
    {
        string[] expected =
            ["status", "--porcelain=v1", "--untracked-files=all", "--", ".", ":(exclude)docs", ":(exclude).claude"];

        Assert.Equal(expected, GitWorkTree.StatusArguments);
    }

    [Fact]
    public void UatResults_UserRun_OmitsAgentRun()
    {
        var json = SerializeResults(agentRun: null);

        Assert.DoesNotContain("agentRun", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UatResults_AgentRun_WritesAgentRun()
    {
        var json = SerializeResults(new AgentRunInfo("D-S239", "CLAUDECODE", TreeClean: true));

        using var document = JsonDocument.Parse(json);
        var agentRun = document.RootElement.GetProperty("agentRun");
        Assert.Equal("D-S239", agentRun.GetProperty("authority").GetString());
        Assert.True(agentRun.GetProperty("treeClean").GetBoolean());
    }

    private static string SerializeResults(AgentRunInfo? agentRun)
    {
        var now = DateTime.UtcNow;
        var results = new UatResults(
            1, "20261027-142233-9f3a", "run", "process", now, now, "0123456789abcdef", false, 22631, "10.0.0",
            agentRun is null ? "user" : "agent", true, "pass", 0, [], CleanupSummary.NotRun(), [], agentRun);
        return JsonSerializer.Serialize(results, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
