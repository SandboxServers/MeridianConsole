using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Stack;

public sealed class LabTargetRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-27T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    public LabTargetRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-lab-registry-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temp root is disposable.
        }
    }

    [Fact]
    public void Register_ContainerWithWrongPrefix_ReturnsFailure()
    {
        var registry = new LabTargetRegistry(LabPaths.FromRoot(_root), new FakeDocker(exitCode: 0), _time);

        Result<LabTarget> result = registry.Register("x:meridian-lab-postgres", LabTargetKind.Container, "meridian-lab-postgres", "abc");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Register_ThenReload_PersistsTargets()
    {
        LabPaths paths = LabPaths.FromRoot(_root);
        var first = new LabTargetRegistry(paths, new FakeDocker(exitCode: 0), _time);
        Result<LabTarget> registered = first.Register("c:meridian-lab-postgres", LabTargetKind.Container, "meridian-lab-postgres", "abc123");
        Assert.True(registered.IsSuccess);

        var second = new LabTargetRegistry(paths, new FakeDocker(exitCode: 0), _time);

        LabTarget? found = second.All.SingleOrDefault(t => t.TargetId == "c:meridian-lab-postgres");
        Assert.NotNull(found);
        Assert.Equal("abc123", found!.ContainerId);
    }

    [Fact]
    public async Task ReconcileAsync_ContainerMissingFromDocker_DropsEntry()
    {
        LabPaths paths = LabPaths.FromRoot(_root);
        var registry = new LabTargetRegistry(paths, new FakeDocker(exitCode: 1), _time);
        _ = registry.Register("c:meridian-lab-redis", LabTargetKind.Container, "meridian-lab-redis", "gone");

        int dropped = await registry.ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, dropped);
        Assert.Empty(registry.All);
    }

    [Fact]
    public async Task ReconcileAsync_DockerUnavailable_KeepsEverything()
    {
        LabPaths paths = LabPaths.FromRoot(_root);
        var registry = new LabTargetRegistry(paths, new FailingDocker(), _time);
        _ = registry.Register("c:meridian-lab-redis", LabTargetKind.Container, "meridian-lab-redis", "kept");

        int dropped = await registry.ReconcileAsync(CancellationToken.None);

        Assert.Equal(0, dropped);
        Assert.Single(registry.All);
    }

    private sealed class FakeDocker(int exitCode) : IDockerCli
    {
        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct) =>
            Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(exitCode, string.Empty, string.Empty)));
    }

    private sealed class FailingDocker : IDockerCli
    {
        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct) =>
            Task.FromResult(Result<DockerOutput>.Failure("[Lab.Docker] unavailable"));
    }
}
