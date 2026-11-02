using Dhadgar.Lab.Build;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Pool;

/// <summary>
/// The Linux container provider against a real Docker engine: acquire starts a labelled member, the probe runs inside it, reset
/// recreates it under the same id with a new container id, and release removes it. Needs the member image (default
/// <c>meridian-lab/linux-member:latest</c>, which <c>lab_build</c> tags; set <c>MERIDIAN_LAB_MEMBER_IMAGE</c> to override) and
/// skips when that image is not built. Creates the lab network when it is missing. Cleanup removes only the member container this
/// test created.
/// </summary>
[Trait("RequiresDocker", "true")]
[Collection(LabDockerGate.Name)]
public sealed class LinuxPoolDockerTests
{
    private const string LinuxContext = "desktop-linux";
    private const string LabNetwork = "meridian-lab-net";
    private const string ProbeDll = "/opt/lab/probe/Dhadgar.Lab.Probe.dll";

    [SkippableFact]
    public async Task Acquire_Reset_Release_RoundTripsARealMember()
    {
        string image = Environment.GetEnvironmentVariable("MERIDIAN_LAB_MEMBER_IMAGE") is { Length: > 0 } set ? set : LabBuilder.LinuxMemberLatestTag;
        var options = new DockerOptions(LinuxContext, null);
        var docker = new DockerCli(options);

        Result<DockerOutput> built = await docker.RunAsync(DockerEngine.Linux, ["image", "inspect", "--format", "{{.Id}}", "--", image], CancellationToken.None);
        Skip.If(built.IsFailure || built.Value.ExitCode != 0, $"member image {image} is not built; run lab_build or set MERIDIAN_LAB_MEMBER_IMAGE");

        string root = Path.Combine(Path.GetTempPath(), "lab-l30-docker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Lab:Pool:LinuxImage"] = image }).Build();
        string? memberId = null;
        try
        {
            _ = await docker.RunAsync(DockerEngine.Linux, ["network", "create", LabNetwork], CancellationToken.None);

            var registry = new LabTargetRegistry(LabPaths.FromRoot(root), docker, TimeProvider.System);
            var guard = new FaultTargetGuard(registry, new DockerInspector(docker, options), new Tripwire());
            var provider = new LinuxContainerProvider(docker, config);
            var manager = new PoolManager(LabPaths.FromRoot(root), [provider], registry, guard, docker, config, new FakeTimeProvider(), NullLogger<PoolManager>.Instance);

            Result<IReadOnlyList<PoolMember>> acquired = await manager.AcquireAsync(
                new PoolConstraints("linux", null, null, null, "container", null, null), "docker test", "lease-docker", CancellationToken.None);
            Assert.True(acquired.IsSuccess, acquired.Error);
            memberId = acquired.Value[0].MemberId;
            Assert.Equal("lin-01", memberId);
            string name = LinuxContainerProvider.ContainerPrefix + memberId;

            string idBefore = await InspectIdAsync(docker, name);

            Result<DockerOutput> probe = await docker.RunAsync(
                DockerEngine.Linux, ["exec", name, "dotnet", ProbeDll, "footprint", "--json"], CancellationToken.None);
            Assert.True(probe.IsSuccess, probe.Error);
            Assert.Equal(2, probe.Value.ExitCode);

            Result<ResolvedTarget> resolved = await guard.ResolveAsync("m:" + memberId, "reset", CancellationToken.None);
            Assert.True(resolved.IsSuccess, resolved.Error);

            Result<PoolMember> reset = await manager.ResetAsync(memberId, CancellationToken.None);
            Assert.True(reset.IsSuccess, reset.Error);
            Assert.Equal(memberId, reset.Value.MemberId);
            string idAfter = await InspectIdAsync(docker, name);
            Assert.NotEqual(idBefore, idAfter);

            Result released = await manager.ReleaseAsync(memberId, CancellationToken.None);
            Assert.True(released.IsSuccess, released.Error);
            Result<DockerOutput> gone = await docker.RunAsync(DockerEngine.Linux, ["inspect", "--format", "{{.Id}}", "--", name], CancellationToken.None);
            Assert.True(gone.IsFailure || gone.Value.ExitCode != 0, "the member container is still present after release");
            memberId = null;
        }
        finally
        {
            if (memberId is not null)
            {
                _ = await docker.RunAsync(DockerEngine.Linux, ["rm", "-f", LinuxContainerProvider.ContainerPrefix + memberId], CancellationToken.None);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Returns the full container id of <paramref name="name"/>, failing the test when docker cannot inspect it.</summary>
    private static async Task<string> InspectIdAsync(IDockerCli docker, string name)
    {
        Result<DockerOutput> inspect = await docker.RunAsync(DockerEngine.Linux, ["inspect", "--format", "{{.Id}}", "--", name], CancellationToken.None);
        Assert.True(inspect.IsSuccess, inspect.Error);
        Assert.Equal(0, inspect.Value.ExitCode);
        return inspect.Value.StdOut.Trim();
    }
}
