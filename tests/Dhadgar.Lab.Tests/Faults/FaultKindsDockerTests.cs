using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Faults.Kinds;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Tests.Faults;

/// <summary>
/// The kill and pause kinds against a real labelled container: the guard resolves it, the fault takes effect, heal undoes it.
/// The container is a throwaway lab-labelled alpine sleeper and is removed by the id this test created.
/// </summary>
[Trait("RequiresDocker", "true")]
public sealed class FaultKindsDockerTests
{
    private const string LinuxContext = "desktop-linux";
    private const string Image = "alpine:3";

    [Fact]
    public async Task Kill_StopsTheContainer_AndHealStartsIt()
    {
        await WithSleeperAsync(async (docker, guard, registry, id) =>
        {
            var fault = new KillFault(docker, registry, guard, new EmptyJournal(), NullLogger<KillFault>.Instance);
            await RunFaultAsync(fault, guard, "kill", JsonSerializer.SerializeToElement(new { signal = "KILL" }), async () =>
            {
                Assert.False(await IsStateAsync(docker, id, "{{.State.Running}}", "true"));
            });
            Assert.True(await IsStateAsync(docker, id, "{{.State.Running}}", "true"));
        });
    }

    [Fact]
    public async Task Pause_FreezesTheContainer_AndHealUnpausesIt()
    {
        await WithSleeperAsync(async (docker, guard, registry, id) =>
        {
            var fault = new PauseFault(docker, registry, guard,NullLogger<PauseFault>.Instance);
            await RunFaultAsync(fault, guard, "pause", JsonSerializer.SerializeToElement(new { }), async () =>
            {
                Assert.True(await IsStateAsync(docker, id, "{{.State.Paused}}", "true"));
            });
            Assert.True(await IsStateAsync(docker, id, "{{.State.Paused}}", "false"));
        });
    }

    private static async Task RunFaultAsync(IFaultKind fault, FaultTargetGuard guard, string kind, JsonElement args, Func<Task> whileFaulted)
    {
        Result<ResolvedTarget> resolved = await guard.ResolveAsync("c:l20-sleeper", kind, CancellationToken.None);
        Assert.True(resolved.IsSuccess, resolved.Error);

        Result<JsonElement> prepared = await fault.PrepareAsync(resolved.Value, args, "f-l20", CancellationToken.None);
        Assert.True(prepared.IsSuccess, prepared.Error);
        Result injected = await fault.InjectAsync(resolved.Value, args, prepared.Value, "f-l20", CancellationToken.None);
        Assert.True(injected.IsSuccess, injected.Error);

        await whileFaulted();

        var entry = new FaultJournalEntry(
            "f-l20", kind, "c:l20-sleeper", args, prepared.Value, FaultState.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), null);
        Result healed = await fault.HealAsync(entry, CancellationToken.None);
        Assert.True(healed.IsSuccess, healed.Error);
    }

    private static async Task WithSleeperAsync(Func<DockerCli, FaultTargetGuard, LabTargetRegistry, string, Task> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "lab-l20-docker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new DockerOptions(LinuxContext, null);
        var docker = new DockerCli(options);
        string? id = null;
        try
        {
            string name = "meridian-lab-l20-" + Guid.NewGuid().ToString("N")[..8];
            var run = new List<string> { "run", "-d", "--name", name, "--label", "meridian-lab=1", Image, "sleep", "600" };
            Result<DockerOutput> started = await docker.RunAsync(DockerEngine.Linux, run, CancellationToken.None);
            Assert.True(started.IsSuccess, started.Error);
            id = started.Value.StdOut.Trim();

            var registry = new LabTargetRegistry(LabPaths.FromRoot(root), docker, TimeProvider.System);
            Result<LabTarget> registered = registry.Register("c:l20-sleeper", LabTargetKind.Container, name, id);
            Assert.True(registered.IsSuccess, registered.Error);

            var guard = new FaultTargetGuard(registry, new DockerInspector(docker, options), new Tripwire());
            await body(docker, guard, registry, id);
        }
        finally
        {
            if (!string.IsNullOrEmpty(id))
            {
                _ = await docker.RunAsync(DockerEngine.Linux, new List<string> { "rm", "-f", id }, CancellationToken.None);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<bool> IsStateAsync(DockerCli docker, string id, string format, string expected)
    {
        var args = new List<string> { "inspect", "-f", format, id };
        Result<DockerOutput> inspected = await docker.RunAsync(DockerEngine.Linux, args, CancellationToken.None);
        return inspected.IsSuccess && string.Equals(inspected.Value.StdOut.Trim(), expected, StringComparison.Ordinal);
    }

    private sealed class EmptyJournal : IFaultJournal
    {
        public Task<Result<IReadOnlyList<FaultJournalEntry>>> ReadAsync(CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<FaultJournalEntry>>.Success([]));

        public Task<Result> WriteAsync(IReadOnlyList<FaultJournalEntry> entries, CancellationToken ct) =>
            Task.FromResult(Result.Success());
    }
}
