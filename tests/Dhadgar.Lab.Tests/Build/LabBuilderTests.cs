using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Build;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dhadgar.Lab.Tests.Build;

public sealed class LabBuilderTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meridian-lab-l09-repo"));

    [Fact]
    public void Restore_UsesLockedMode_AndNeverRuntimeIdentifier()
    {
        IReadOnlyList<string> args = LabBuilder.RestoreArgs("src/Dhadgar.Nodes/Dhadgar.Nodes.csproj");

        Assert.Contains("--locked-mode", args);
        Assert.DoesNotContain("-r", args);
        Assert.DoesNotContain("--runtime", args);
    }

    [Fact]
    public void Publish_UsesNoRestore_AndNeverRuntimeIdentifier()
    {
        IReadOnlyList<string> args = LabBuilder.PublishArgs("src/Dhadgar.Gateway/Dhadgar.Gateway.csproj", "out");

        Assert.Contains("--no-restore", args);
        Assert.DoesNotContain("-r", args);
        Assert.DoesNotContain("--runtime", args);
    }

    [Fact]
    public void ResolveProject_UnknownName_IsInvalidArgument()
    {
        var result = LabBuilder.ResolveProject("agent_windows");

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveProject_KnownName_Succeeds()
    {
        var result = LabBuilder.ResolveProject("nodes");

        Assert.True(result.IsSuccess);
        Assert.Equal("Dhadgar.Nodes.dll", result.Value!.DllName);
    }

    [Fact]
    public void ValidateCheckout_InsideWorktrees_IsAccepted()
    {
        string checkout = Path.Combine(RepoRoot, ".claude", "worktrees", "l09");

        var result = LabBuilder.ValidateCheckout(checkout, RepoRoot, new PathValidator(NullLogger<PathValidator>.Instance));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public void ValidateCheckout_OutsideRepo_IsRefused()
    {
        string checkout = Path.Combine(Path.GetTempPath(), "elsewhere-l09");

        var result = LabBuilder.ValidateCheckout(checkout, RepoRoot, new PathValidator(NullLogger<PathValidator>.Instance));

        Assert.True(result.IsFailure);
        Assert.StartsWith("[Lab.InvalidArgument]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCheckout_DotDot_IsRefused()
    {
        string checkout = RepoRoot + "\\.claude\\..\\..\\elsewhere";

        var result = LabBuilder.ValidateCheckout(checkout, RepoRoot, new PathValidator(NullLogger<PathValidator>.Instance));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void BuildId_DirtyTree_AddsTimestampSuffix()
    {
        var at = new DateTimeOffset(2026, 10, 27, 20, 41, 5, TimeSpan.Zero);

        Assert.Equal("a9c623a1", LabBuilder.BuildId("a9c623a1234567890", dirty: false, at));
        Assert.Equal("a9c623a1-dirty-20261027204105", LabBuilder.BuildId("a9c623a1234567890", dirty: true, at));
    }

    [Fact]
    public void DockerBuild_CarriesLabLabelAndProjectArg()
    {
        IReadOnlyList<string> args = LabBuilder.DockerBuildArgs(
            LabBuilder.ImageTag("nodes", "a9c623a1"), "Dhadgar.Nodes.dll", "C:\\pub", "tools/lab/images/dotnet-service.Dockerfile");

        Assert.Contains("meridian-lab=1", args);
        Assert.Contains("PROJECT_DLL=Dhadgar.Nodes.dll", args);
        Assert.Contains("meridian-lab/nodes:a9c623a1", args);
    }

    [Fact]
    public void IsLockDrift_DetectsNu1004()
    {
        Assert.True(LabBuilder.IsLockDrift("error NU1004: The locked file is out of date"));
        Assert.False(LabBuilder.IsLockDrift("Restore succeeded."));
    }
}
