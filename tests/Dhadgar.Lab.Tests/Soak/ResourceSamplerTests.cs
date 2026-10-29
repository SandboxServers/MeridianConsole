using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Soak;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Lab.Tests.Soak;

/// <summary>
/// The soak resource sampler over fakes: only labelled registered containers are passed by id, docker is never called
/// without ids, and an unreadable sample is a failure.
/// </summary>
public sealed class ResourceSamplerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sample_PassesOnlyLabelledRegisteredContainerIds()
    {
        var docker = new FakeDocker(
            """{"Name":"ml-a","MemUsage":"512MiB / 1GiB","PIDs":"12"}""");
        var registry = new FakeRegistry([("ml-a", "cid-a"), ("ml-b", "cid-b")]);
        var sampler = new ResourceSampler(docker, registry, ct => Task.FromResult(Result<IReadOnlyList<string>>.Success(["ml-a"])), new FakeTimeProvider(T0));

        Result<ResourceSampleSet> sample = await sampler.SampleAsync(CancellationToken.None);

        Assert.True(sample.IsSuccess);
        Assert.Equal("stats", docker.Args[0]);
        Assert.Contains("cid-a", docker.Args);
        Assert.DoesNotContain("cid-b", docker.Args);
        Assert.DoesNotContain("--all", docker.Args);
        Assert.Equal(512d, sample.Value!.Values["memory_mib:ml-a"], 3);
        Assert.Equal(12d, sample.Value.Values["pids:ml-a"], 3);
    }

    [Fact]
    public async Task Sample_NoLabelledContainers_NeverCallsDockerWithoutIds()
    {
        var docker = new FakeDocker(string.Empty);
        var registry = new FakeRegistry([("ml-a", "cid-a")]);
        var sampler = new ResourceSampler(docker, registry, ct => Task.FromResult(Result<IReadOnlyList<string>>.Success([])), new FakeTimeProvider(T0));

        Result<ResourceSampleSet> sample = await sampler.SampleAsync(CancellationToken.None);

        Assert.True(sample.IsSuccess);
        Assert.Equal(0, docker.Calls);
        Assert.Empty(sample.Value!.Values);
    }

    [Fact]
    public async Task Sample_DockerExitNonZero_IsFailure()
    {
        var docker = new FakeDocker("", exitCode: 1);
        var registry = new FakeRegistry([("ml-a", "cid-a")]);
        var sampler = new ResourceSampler(docker, registry, ct => Task.FromResult(Result<IReadOnlyList<string>>.Success(["ml-a"])), new FakeTimeProvider(T0));

        Result<ResourceSampleSet> sample = await sampler.SampleAsync(CancellationToken.None);

        Assert.True(sample.IsFailure);
    }

    [Fact]
    public async Task Sample_UnreadableLine_IsFailure()
    {
        var docker = new FakeDocker("not json");
        var registry = new FakeRegistry([("ml-a", "cid-a")]);
        var sampler = new ResourceSampler(docker, registry, ct => Task.FromResult(Result<IReadOnlyList<string>>.Success(["ml-a"])), new FakeTimeProvider(T0));

        Result<ResourceSampleSet> sample = await sampler.SampleAsync(CancellationToken.None);

        Assert.True(sample.IsFailure);
    }

    [Fact]
    public async Task Sample_DaemonHandles_AreRecorded()
    {
        var docker = new FakeDocker(string.Empty);
        var registry = new FakeRegistry([]);
        var sampler = new ResourceSampler(docker, registry, ct => Task.FromResult(Result<IReadOnlyList<string>>.Success([])), new FakeTimeProvider(T0), () => 240);

        Result<ResourceSampleSet> sample = await sampler.SampleAsync(CancellationToken.None);

        Assert.Equal(240d, sample.Value!.Values[ResourceSampler.DaemonHandlesKey], 3);
        Assert.Equal(0, docker.Calls);
    }

    [Theory]
    [InlineData("1GiB", 1024d)]
    [InlineData("512MiB", 512d)]
    [InlineData("2048KiB", 2d)]
    [InlineData("1000kB", 1000d * 1000d / (1024d * 1024d))]
    public void MemoryMiB_ReadsDockerUnits(string usage, double expected)
    {
        Result<double> mib = ResourceSampler.MemoryMiB(usage + " / 7.67GiB");

        Assert.True(mib.IsSuccess);
        Assert.Equal(expected, mib.Value, 6);
    }

    [Fact]
    public void MemoryMiB_UnknownUnit_IsFailure()
    {
        Assert.True(ResourceSampler.MemoryMiB("12 furlongs / 1GiB").IsFailure);
    }

    private sealed class FakeDocker(string stdout, int exitCode = 0) : IDockerCli
    {
        public IReadOnlyList<string> Args { get; private set; } = [];

        public int Calls { get; private set; }

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls++;
            Args = args;
            return Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(exitCode, stdout, string.Empty)));
        }
    }

    private sealed class FakeRegistry(IReadOnlyList<(string Name, string ContainerId)> containers) : ILabTargetRegistry
    {
        public IReadOnlyList<LabTarget> All { get; } = containers
            .Select(c => new LabTarget("c:" + c.Name, LabTargetKind.Container, c.Name, c.ContainerId, T0))
            .ToArray();

        public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId) =>
            throw new NotSupportedException();

        public LabTarget? Find(string targetId) => All.FirstOrDefault(target => target.TargetId == targetId);

        public void Remove(string targetId) => throw new NotSupportedException();

        public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
