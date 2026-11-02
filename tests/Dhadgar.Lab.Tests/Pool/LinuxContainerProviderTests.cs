using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Pool;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;

namespace Dhadgar.Lab.Tests.Pool;

/// <summary>
/// The Linux container provider against a scripted <see cref="IDockerCli"/>: no Docker engine is needed. Covers the image tag
/// <c>lab_build</c> writes, the image argument guard, and the cleanup of a failed or cancelled <c>docker run</c>.
/// </summary>
public sealed class LinuxContainerProviderTests
{
    private const string MemberId = "lin-01";
    private const string ContainerName = "meridian-lab-m-lin-01";

    [Fact]
    public void LinuxMemberLatestTagArgs_TagsTheBuildTagAsLatest()
    {
        IReadOnlyList<string> args = LabBuilder.LinuxMemberLatestTagArgs("meridian-lab/linux-member:abc123");

        Assert.Equal(["tag", "meridian-lab/linux-member:abc123", "meridian-lab/linux-member:latest"], args);
        Assert.Equal(LabBuilder.LinuxMemberLatestTag, args[2]);
    }

    [Theory]
    [InlineData("-v")]
    [InlineData("--privileged")]
    [InlineData("x y")]
    public async Task Provision_ImageSettingThatIsNotAnImageReference_NeverReachesTheRunArgs(string configured)
    {
        var docker = new ScriptedDockerCli();
        var provider = new LinuxContainerProvider(docker, ConfigWithImage(configured));

        Result<PoolMember> provisioned = await provider.ProvisionAsync(AnyConstraints, MemberId, CancellationToken.None);

        Assert.True(provisioned.IsSuccess, provisioned.Error);
        string[] run = docker.Calls.Single(args => args[0] == "run");
        Assert.DoesNotContain(configured, run);
        Assert.DoesNotContain("-v", run);
        Assert.DoesNotContain("--privileged", run);
        Assert.Equal(LabBuilder.LinuxMemberLatestTag, run[^1]);
        Assert.Equal("--", run[^2]);
    }

    [Fact]
    public async Task Provision_ConfiguredImageReference_IsTheLastRunArgument()
    {
        var docker = new ScriptedDockerCli();
        var provider = new LinuxContainerProvider(docker, ConfigWithImage("meridian-lab/linux-member:abc123"));

        _ = await provider.ProvisionAsync(AnyConstraints, MemberId, CancellationToken.None);

        string[] run = docker.Calls.Single(args => args[0] == "run");
        Assert.Equal("meridian-lab/linux-member:abc123", run[^1]);
    }

    [Fact]
    public async Task Provision_FailedRun_RemovesTheContainerWhenItCarriesThisMembersLabels()
    {
        var docker = new ScriptedDockerCli { RunExit = 125, InspectOutput = "1 " + MemberId };
        var provider = new LinuxContainerProvider(docker, ConfigWithImage(null));

        Result<PoolMember> provisioned = await provider.ProvisionAsync(AnyConstraints, MemberId, CancellationToken.None);

        Assert.True(provisioned.IsFailure);
        Assert.Contains(docker.Calls, args => args.SequenceEqual(new[] { "rm", "-f", ContainerName }));
    }

    [Fact]
    public async Task Provision_FailedRun_LeavesAForeignContainerWithTheSameName()
    {
        var docker = new ScriptedDockerCli { RunExit = 125, InspectOutput = "0 someone-else" };
        var provider = new LinuxContainerProvider(docker, ConfigWithImage(null));

        Result<PoolMember> provisioned = await provider.ProvisionAsync(AnyConstraints, MemberId, CancellationToken.None);

        Assert.True(provisioned.IsFailure);
        Assert.DoesNotContain(docker.Calls, args => args[0] == "rm");
    }

    [Fact]
    public async Task Provision_CancelledRun_RemovesItsOwnContainerAndRethrows()
    {
        var docker = new ScriptedDockerCli { ThrowOnRun = true, InspectOutput = "1 " + MemberId };
        var provider = new LinuxContainerProvider(docker, ConfigWithImage(null));

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.ProvisionAsync(AnyConstraints, MemberId, CancellationToken.None));

        Assert.Contains(docker.Calls, args => args.SequenceEqual(new[] { "rm", "-f", ContainerName }));
    }

    private static PoolConstraints AnyConstraints { get; } = new(null, null, null, null, null, null, null);

    private static IConfiguration ConfigWithImage(string? image)
    {
        var values = new Dictionary<string, string?>();
        if (image is not null)
        {
            values["Lab:Pool:LinuxImage"] = image;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Answers <c>run</c> with <see cref="RunExit"/> (or throws the cancel when <see cref="ThrowOnRun"/> is set), and <c>inspect</c> with
    /// <see cref="InspectOutput"/>. Every call is recorded.
    /// </summary>
    private sealed class ScriptedDockerCli : IDockerCli
    {
        public int RunExit { get; set; }

        public bool ThrowOnRun { get; set; }

        public string InspectOutput { get; set; } = string.Empty;

        public List<string[]> Calls { get; } = [];

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            if (args[0] == "run" && ThrowOnRun)
            {
                throw new OperationCanceledException();
            }

            DockerOutput output = args[0] switch
            {
                "run" => new DockerOutput(RunExit, string.Empty, RunExit == 0 ? string.Empty : "docker run failed"),
                "inspect" => new DockerOutput(0, InspectOutput, string.Empty),
                _ => new DockerOutput(0, string.Empty, string.Empty),
            };
            return Task.FromResult(Result<DockerOutput>.Success(output));
        }
    }
}
