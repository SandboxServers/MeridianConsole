using System.Diagnostics;
using System.Reflection;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab;
using Dhadgar.Lab.Build;
using Dhadgar.Shared.Results;
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
    public void DockerfileFor_Probe_IsProbeDockerfile_AndGatewayIsDotnetService()
    {
        Assert.Equal("probe.Dockerfile", LabBuilder.DockerfileFor("probe"));
        Assert.Equal("dotnet-service.Dockerfile", LabBuilder.DockerfileFor("gateway"));
        Assert.Equal("dotnet-service.Dockerfile", LabBuilder.DockerfileFor("notifications"));
    }

    [Fact]
    public void IsLockDrift_DetectsNu1004()
    {
        Assert.True(LabBuilder.IsLockDrift("error NU1004: The locked file is out of date"));
        Assert.False(LabBuilder.IsLockDrift("Restore succeeded."));
    }

    [Fact]
    public async Task RunAsync_CallerCancelled_KillsTheChildAndRethrows()
    {
        // A long-running child stands in for MSBuild. The caller's cancellation must stop the child (N2), not leave it running.
        string process = OperatingSystem.IsWindows() ? "PING" : "sleep";
        int before = Process.GetProcessesByName(process).Length;
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "ping -n 60 127.0.0.1 > nul" }
            : new List<string> { "60" };
        string fileName = OperatingSystem.IsWindows() ? "cmd" : "sleep";
        MethodInfo run = typeof(LabBuilder).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("LabBuilder.RunAsync not found");

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
        var task = (Task<Result<string>>)run.Invoke(null, [fileName, args, Environment.CurrentDirectory, cancel.Token])!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        // The kill is immediate but the OS reaps the child asynchronously; allow a few seconds.
        for (int i = 0; i < 50 && Process.GetProcessesByName(process).Length > before; i++)
        {
            await Task.Delay(100);
        }

        Assert.True(Process.GetProcessesByName(process).Length <= before);
    }

    [Fact]
    public async Task RunAsync_ChildDoesNotInheritTheDaemonToken()
    {
        const string secret = "lab-token-must-not-reach-a-child";
        string? previous = Environment.GetEnvironmentVariable(LabDefaults.TokenEnvVar);
        Environment.SetEnvironmentVariable(LabDefaults.TokenEnvVar, secret);
        try
        {
            string fileName = OperatingSystem.IsWindows() ? "cmd" : "sh";
            var args = OperatingSystem.IsWindows()
                ? new List<string> { "/c", "echo %" + LabDefaults.TokenEnvVar + "%" }
                : new List<string> { "-c", "printf %s \"$" + LabDefaults.TokenEnvVar + "\"" };
            MethodInfo run = typeof(LabBuilder).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("LabBuilder.RunAsync not found");

            var task = (Task<Result<string>>)run.Invoke(null, [fileName, args, Environment.CurrentDirectory, CancellationToken.None])!;
            Result<string> result = await task;

            Assert.True(result.IsSuccess, result.Error);
            Assert.DoesNotContain(secret, result.Value!, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LabDefaults.TokenEnvVar, previous);
        }
    }
}
