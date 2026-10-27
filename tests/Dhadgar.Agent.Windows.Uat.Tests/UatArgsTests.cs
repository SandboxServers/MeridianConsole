using Dhadgar.Agent.Windows.Uat;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class UatArgsTests
{
    private const string RunId = "20261027-142233-9f3a";

    [Fact]
    public void Parse_ValidRun_Succeeds()
    {
        var result = UatArgs.Parse(["run", "--repo-root", "C:\\repo", "--run-id", RunId, "--mode", "process", "--accept-machine-changes"]);

        Assert.True(result.IsSuccess);
        Assert.Equal("run", result.Value.Verb);
        Assert.Equal("process", result.Value.Mode);
        Assert.True(result.Value.AcceptMachineChanges);
    }

    [Fact]
    public void Parse_RunWithoutAcceptMachineChanges_Fails()
    {
        var result = UatArgs.Parse(["run", "--repo-root", "C:\\repo", "--run-id", RunId, "--mode", "process"]);

        Assert.True(result.IsFailure);
        Assert.Contains("--accept-machine-changes", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("20261027-142233-9F3A")]
    [InlineData("../../etc")]
    [InlineData("20261027142233-9f3a")]
    public void Parse_BadRunId_Fails(string runId)
    {
        var result = UatArgs.Parse(["preflight", "--repo-root", "C:\\repo", "--run-id", runId]);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Parse_UnknownArgument_Fails()
    {
        var result = UatArgs.Parse(["preflight", "--repo-root", "C:\\repo", "--run-id", RunId, "--force"]);

        Assert.True(result.IsFailure);
        Assert.Contains("unknown argument", result.Error, StringComparison.Ordinal);
    }
}
