using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Lab.Tests.Processes;

[Collection("LabProcesses")]
public sealed class ContainerLogFollowerTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 27, 9, 55, 0, TimeSpan.Zero);

    [Fact]
    public void BuildSpec_LabContainer_UsesDockerContextAndNoRelaunch()
    {
        using var harness = new SupervisorHarness();
        var follower = CreateFollower(harness, new Dictionary<string, string?>());

        Result<LabProcessSpec> built = follower.BuildSpec("api", "meridian-lab-api-1", Since);

        Assert.True(built.IsSuccess, built.Error);
        LabProcessSpec spec = built.Value!;
        Assert.Equal("api", spec.Name);
        Assert.Equal("docker", spec.FileName);
        Assert.Equal(
            new[] { "--context", "desktop-linux", "logs", "-f", "--since", Since.ToString("O", System.Globalization.CultureInfo.InvariantCulture), "meridian-lab-api-1" },
            spec.Arguments);
        Assert.False(spec.Relaunch);
    }

    [Fact]
    public void BuildSpec_ConfiguredContext_IsUsed()
    {
        using var harness = new SupervisorHarness();
        var follower = CreateFollower(harness, new Dictionary<string, string?> { [ContainerLogFollower.LinuxContextKey] = "lab-linux" });

        Result<LabProcessSpec> built = follower.BuildSpec("api", "meridian-lab-api-1", Since);

        Assert.True(built.IsSuccess, built.Error);
        Assert.Equal("lab-linux", built.Value!.Arguments[1]);
    }

    [Fact]
    public void BuildSpec_NonLabContainer_IsRefused()
    {
        using var harness = new SupervisorHarness();
        var follower = CreateFollower(harness, new Dictionary<string, string?>());

        Result<LabProcessSpec> built = follower.BuildSpec("api", "meridian-dev-api-1", Since);

        Assert.True(built.IsFailure);
        Assert.Contains("[Lab.BlastRadius]", built.Error);
    }

    [Fact]
    public void BuildSpec_InvalidContextName_IsRefused()
    {
        using var harness = new SupervisorHarness();
        var follower = CreateFollower(harness, new Dictionary<string, string?> { [ContainerLogFollower.LinuxContextKey] = "bad context;rm" });

        Result<LabProcessSpec> built = follower.BuildSpec("api", "meridian-lab-api-1", Since);

        Assert.True(built.IsFailure);
    }

    [SkippableFact]
    public async Task Follower_ExitingOnItsOwn_IsNotCountedAsRestart()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        using var harness = new SupervisorHarness();
        LabProcessSpec spec = SupervisorHarness.ChildSpec("tc-follower", relaunch: false, "--exit", "3");

        _ = await harness.Supervisor.StartAsync(spec, CancellationToken.None);

        bool exited = await SupervisorHarness.WaitUntilAsync(() => harness.Supervisor.Snapshot().Single().State == "exited", TimeSpan.FromSeconds(30));
        Assert.True(exited);
        Assert.Equal(0, harness.Supervisor.Snapshot().Single().Restarts);
    }

    private static ContainerLogFollower CreateFollower(SupervisorHarness harness, Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ContainerLogFollower(harness.Supervisor, configuration, harness.Time, harness.Paths);
    }
}
