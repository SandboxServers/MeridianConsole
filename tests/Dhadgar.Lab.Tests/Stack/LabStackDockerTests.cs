using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Secrets;
using Dhadgar.Lab.Stack;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Stack;

/// <summary>
/// Drives the real lab stack against the Docker engine behind the <c>desktop-linux</c> context. Runs only where
/// Docker and the network are available. Tears down with wipe in <c>finally</c>.
/// </summary>
[Collection("LabDockerStack")]
public sealed class LabStackDockerTests
{
    private const string LinuxContext = "desktop-linux";

    [Fact]
    [Trait("RequiresDocker", "true")]
    [Trait("RequiresNetwork", "true")]
    public async Task UpThenDown_RealEngine_CreatesAndRemovesOnlyLabContainers()
    {
        string repoRoot = FindRepoRoot();
        string home = Path.Combine(Path.GetTempPath(), "meridian-lab-docker-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(home);
        var paths = LabPaths.FromRoot(home, repoRoot);
        var docker = new DockerCli(new DockerOptions(LinuxContext, null));
        using var vault = new SecretVault();
        using var toxi = new ToxiproxyClient();
        var targets = new LabTargetRegistry(paths, docker, TimeProvider.System);
        using var stack = new LabStack(
            docker,
            targets,
            toxi,
            vault,
            new LabCredentialRefs(),
            paths,
            new PathValidator(NullLogger<PathValidator>.Instance),
            TimeProvider.System);

        try
        {
            Result<StackStatusDto> up = await stack.UpAsync(wipe: true, CancellationToken.None);

            Assert.True(up.IsSuccess, up.IsFailure ? up.Error : string.Empty);
            Assert.All(up.Value.Containers, c => Assert.StartsWith("meridian-lab-", c.Name, StringComparison.Ordinal));
        }
        finally
        {
            _ = await stack.DownAsync(wipe: true, CancellationToken.None);
            Directory.Delete(home, recursive: true);
        }
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "tools", "lab", "images", "rabbitmq.Dockerfile")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Repository root with tools/lab/images was not found above the test binaries.");
    }
}
