using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The guard against real containers: a forged registry entry for an unlabelled container, and an unregistered labelled container,
/// are both refused, and neither is touched. Cleanup removes the two containers by the ids this test created.
/// </summary>
[Trait("RequiresDocker", "true")]
[Collection(LabDockerGate.Name)]
public sealed class BlastRadiusDockerTests
{
    private const string LinuxContext = "desktop-linux";
    private const string Image = "alpine:3";

    [Fact]
    public async Task DecoyContainers_AreRefused_AndKeepRunning()
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-l19-docker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new DockerOptions(LinuxContext, null);
        var docker = new DockerCli(options);
        string? labelledId = null;
        string? unlabelledId = null;
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];
            labelledId = await StartSleeperAsync(docker, $"meridian-lab-decoy-{suffix}", labelled: true);
            unlabelledId = await StartSleeperAsync(docker, $"dhadgar-decoy-{suffix}", labelled: false);

            var registry = new LabTargetRegistry(LabPaths.FromRoot(root), docker, TimeProvider.System);
            Result<LabTarget> forged = registry.Register("c:decoy", LabTargetKind.Container, "dhadgar-decoy", unlabelledId);
            Assert.True(forged.IsSuccess, forged.Error);

            var guard = new FaultTargetGuard(registry, new DockerInspector(docker, options), new Tripwire());

            Result<ResolvedTarget> forgedRefusal = await guard.ResolveAsync("c:decoy", "kill", CancellationToken.None);
            Assert.True(forgedRefusal.IsFailure);
            Assert.StartsWith("[Lab.BlastRadius]", forgedRefusal.Error, StringComparison.Ordinal);

            Result<ResolvedTarget> unregisteredRefusal = await guard.ResolveAsync("c:meridian-lab-decoy", "kill", CancellationToken.None);
            Assert.True(unregisteredRefusal.IsFailure);
            Assert.StartsWith("[Lab.BlastRadius]", unregisteredRefusal.Error, StringComparison.Ordinal);

            Assert.True(await IsRunningAsync(docker, labelledId));
            Assert.True(await IsRunningAsync(docker, unlabelledId));
        }
        finally
        {
            await RemoveByIdAsync(docker, labelledId);
            await RemoveByIdAsync(docker, unlabelledId);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<string> StartSleeperAsync(DockerCli docker, string name, bool labelled)
    {
        var args = new List<string> { "run", "-d", "--name", name };
        if (labelled)
        {
            args.Add("--label");
            args.Add("meridian-lab=1");
        }

        args.Add(Image);
        args.Add("sleep");
        args.Add("600");

        Result<DockerOutput> started = await docker.RunAsync(DockerEngine.Linux, args, CancellationToken.None);
        Assert.True(started.IsSuccess, started.Error);
        return started.Value.StdOut.Trim();
    }

    private static async Task<bool> IsRunningAsync(DockerCli docker, string id)
    {
        var args = new List<string> { "inspect", "-f", "{{.State.Running}}", id };
        Result<DockerOutput> inspected = await docker.RunAsync(DockerEngine.Linux, args, CancellationToken.None);
        return inspected.IsSuccess && string.Equals(inspected.Value.StdOut.Trim(), "true", StringComparison.Ordinal);
    }

    private static async Task RemoveByIdAsync(DockerCli docker, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var args = new List<string> { "rm", "-f", id };
        _ = await docker.RunAsync(DockerEngine.Linux, args, CancellationToken.None);
    }
}
