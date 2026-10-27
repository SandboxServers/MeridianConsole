using Dhadgar.Agent.Windows.Uat.Infra;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class RunPathsTests
{
    private const string RunId = "20261027-142233-9f3a";
    private static readonly string Repo = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "uat-repo"));

    [Fact]
    public void Validate_ChildOfRunFolder_Succeeds()
    {
        var paths = new RunPaths(Repo, RunId);

        Assert.True(paths.Validate(Path.Combine(paths.RunDir, "logs", "process.log")).IsSuccess);
    }

    [Fact]
    public void Validate_DotDotEscape_Fails()
    {
        var paths = new RunPaths(Repo, RunId);

        Assert.True(paths.Validate(Path.Combine(paths.RunDir, "..", "other")).IsFailure);
    }

    [Fact]
    public void Validate_SiblingRunFolder_Fails()
    {
        var paths = new RunPaths(Repo, RunId);
        var sibling = Path.Combine(Repo, ".uat", "agent-windows", "20261027-142233-aaaa", "x.txt");

        Assert.True(paths.Validate(sibling).IsFailure);
    }

    [Fact]
    public void Validate_NullCharacter_Fails()
    {
        var paths = new RunPaths(Repo, RunId);

        Assert.True(paths.Validate(Path.Combine(paths.RunDir, "a\0b")).IsFailure);
    }

    [Fact]
    public void Validate_RelativePath_Fails()
    {
        var paths = new RunPaths(Repo, RunId);

        Assert.True(paths.Validate(Path.Combine("logs", "process.log")).IsFailure);
    }
    [Fact]
    public void StackImages_AreTheNodesAndGatewayReleaseBuilds()
    {
        var paths = new RunPaths(Repo, RunId);
        var repo = Path.GetFullPath(Repo);

        Assert.Equal(
            [
                Path.Combine(repo, "src", "Dhadgar.Nodes", "bin", "Release", "net10.0", "Dhadgar.Nodes.exe"),
                Path.Combine(repo, "src", "Dhadgar.Gateway", "bin", "Release", "net10.0", "Dhadgar.Gateway.exe"),
            ],
            paths.StackImages);
        Assert.All(paths.StackImages, image => Assert.True(paths.Validate(image).IsFailure));
    }
}