using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Pool;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Pool;

/// <summary>
/// The Linux container provider against a real Docker engine: acquire starts a labelled member, reset recreates it under the same
/// id, and release removes it. Needs the member image (set <c>MERIDIAN_LAB_MEMBER_IMAGE</c>, default <c>alpine:3</c>) and creates
/// the lab network when it is missing. Cleanup removes only the member containers this test created.
/// </summary>
[Trait("RequiresDocker", "true")]
[Collection(LabDockerGate.Name)]
public sealed class LinuxPoolDockerTests
{
    private const string LinuxContext = "desktop-linux";
    private const string LabNetwork = "meridian-lab-net";

    [Fact]
    public async Task Acquire_Reset_Release_RoundTripsARealMember()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-l30-docker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new DockerOptions(LinuxContext, null);
        var docker = new DockerCli(options);
        string image = Environment.GetEnvironmentVariable("MERIDIAN_LAB_MEMBER_IMAGE") is { Length: > 0 } set ? set : "alpine:3";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Lab:Pool:LinuxImage"] = image }).Build();
        string? memberId = null;
        try
        {
            _ = await docker.RunAsync(DockerEngine.Linux, ["network", "create", LabNetwork], CancellationToken.None);

            var registry = new LabTargetRegistry(LabPaths.FromRoot(root), docker, TimeProvider.System);
            var guard = new FaultTargetGuard(registry, new DockerInspector(docker, options), new Tripwire());
            var provider = new LinuxContainerProvider(docker, config);
            var manager = new PoolManager(LabPaths.FromRoot(root), [provider], registry, guard, config, new FakeTimeProvider(), NullLogger<PoolManager>.Instance);

            Result<IReadOnlyList<PoolMember>> acquired = await manager.AcquireAsync(
                new PoolConstraints("linux", null, null, null, "container", null, null), "docker test", "lease-docker", CancellationToken.None);
            Assert.True(acquired.IsSuccess, acquired.Error);
            memberId = acquired.Value[0].MemberId;
            Assert.Equal("lin-01", memberId);

            Result<ResolvedTarget> resolved = await guard.ResolveAsync("m:" + memberId, "reset", CancellationToken.None);
            Assert.True(resolved.IsSuccess, resolved.Error);

            Result<PoolMember> reset = await manager.ResetAsync(memberId, CancellationToken.None);
            Assert.True(reset.IsSuccess, reset.Error);
            Assert.Equal(memberId, reset.Value.MemberId);

            Result released = await manager.ReleaseAsync(memberId, CancellationToken.None);
            Assert.True(released.IsSuccess, released.Error);
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
}
