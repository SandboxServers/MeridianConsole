using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Lab.Invariants;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Tests.Invariants;

/// <summary>
/// I5 over fakes: unregistered labelled containers, failure queues with messages, disabled proxies with no proxy fault and
/// orphan backup folders are violations; the docker ps argument list is fixed.
/// </summary>
public sealed class I5OrphanInvariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly InvariantWindow Window = new(T0.AddMinutes(-5), T0, null, null);

    [Fact]
    public async Task I5_ContainerNotInRegistry_Violation()
    {
        var check = Build(containers: ["meridian-lab-nodes", "meridian-lab-stray"], registered: ["meridian-lab-nodes"]);

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);
        Assert.Equal("orphan-container", violation.CheckId);
        Assert.Equal("meridian-lab-stray", violation.Service);
    }

    [Fact]
    public async Task I5_ErrorQueueWithMessages_Violation_EmptyQueueClean()
    {
        var check = Build(queues: [new BrokerQueueInfo("nodes.enroll_error", 3), new BrokerQueueInfo("nodes.enroll_skipped", 0), new BrokerQueueInfo("nodes.enroll", 9)]);

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);
        Assert.Equal("queue-backlog", violation.CheckId);
        Assert.Equal("nodes.enroll_error", violation.Service);
    }

    [Fact]
    public async Task I5_DisabledProxyWithoutProxyFault_Violation_WithFaultExplained()
    {
        var unexplained = Build(proxies: [new ProxySpec("nodes-postgres", "127.0.0.1:18500", "pg:5432", false)]);
        InvariantViolation violation = Assert.Single((await unexplained.CheckAsync(Window, CancellationToken.None)).Value!);
        Assert.Equal("disabled-proxy-unexplained", violation.CheckId);

        var explained = Build(
            proxies: [new ProxySpec("nodes-postgres", "127.0.0.1:18500", "pg:5432", false)],
            faults: [new ActiveFault("f1", "proxy_disable", "d:nodes", T0, T0, default, null)]);
        Assert.Empty((await explained.CheckAsync(Window, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task I5_BackupFolderWithoutActiveFault_Violation()
    {
        var check = Build(backups: ["f1", "f9"], faults: [new ActiveFault("f1", "toxic", "d:nodes", T0, T0, default, null)]);

        InvariantViolation violation = Assert.Single((await check.CheckAsync(Window, CancellationToken.None)).Value!);
        Assert.Equal("orphan-fault-backup", violation.CheckId);
        Assert.Equal("f9", violation.Service);
    }

    [Fact]
    public async Task I5_SourceFails_ReturnsFailure()
    {
        var check = Build(queuesFail: true);

        Result<IReadOnlyList<InvariantViolation>> result = await check.CheckAsync(Window, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task I5_ListLabelledContainers_UsesFixedArgumentList()
    {
        var docker = new FakeDocker("meridian-lab-nodes\nmeridian-lab-web\n");

        IReadOnlyList<string> names = (await I5OrphanInvariant.ListLabelledContainersAsync(docker, DockerEngine.Linux, CancellationToken.None)).Value!;

        Assert.Equal(["meridian-lab-nodes", "meridian-lab-web"], names);
        Assert.Equal(["ps", "-a", "--filter", "label=meridian-lab=1", "--format", "{{.Names}}"], docker.Args);
    }

    [Fact]
    public async Task I5_StackNotCreated_SkipsQueuesAndProxies_StillReportsOrphanContainer()
    {
        Func<CancellationToken, Task<Result<IReadOnlyList<BrokerQueueInfo>>>> queues = _ => throw new InvalidOperationException("queues must not be read");
        Func<CancellationToken, Task<Result<IReadOnlyList<ProxySpec>>>> proxies = _ => throw new InvalidOperationException("proxies must not be read");
        var check = new I5OrphanInvariant(
            _ => Task.FromResult(Result<IReadOnlyList<string>>.Success(["meridian-lab-stray"])),
            new FakeRegistry([]),
            queues,
            proxies,
            () => [],
            () => [],
            stackCreated: () => false);

        Result<InvariantOutcome> result = await check.CheckWithSkipsAsync(Window, CancellationToken.None);

        Assert.True(result.IsSuccess);
        InvariantViolation violation = Assert.Single(result.Value.Violations);
        Assert.Equal("orphan-container", violation.CheckId);
        Assert.Equal(new[] { "queues", "proxies" }, result.Value.Skipped.Select(s => s.Source).ToArray());
    }

    private static I5OrphanInvariant Build(
        IReadOnlyList<string>? containers = null,
        IReadOnlyList<string>? registered = null,
        IReadOnlyList<BrokerQueueInfo>? queues = null,
        IReadOnlyList<ProxySpec>? proxies = null,
        IReadOnlyList<ActiveFault>? faults = null,
        IReadOnlyList<string>? backups = null,
        bool queuesFail = false)
    {
        IReadOnlyList<string> containerNames = containers ?? [];
        IReadOnlyList<string> registeredNames = registered ?? [];
        IReadOnlyList<BrokerQueueInfo> queueRows = queues ?? [];
        IReadOnlyList<ProxySpec> proxyRows = proxies ?? [];
        IReadOnlyList<ActiveFault> faultRows = faults ?? [];
        IReadOnlyList<string> backupRows = backups ?? [];

        return new I5OrphanInvariant(
            _ => Task.FromResult(Result<IReadOnlyList<string>>.Success(containerNames)),
            new FakeRegistry(registeredNames),
            _ => Task.FromResult(queuesFail
                ? Result<IReadOnlyList<BrokerQueueInfo>>.Failure("[Lab.Http] management api down")
                : Result<IReadOnlyList<BrokerQueueInfo>>.Success(queueRows)),
            _ => Task.FromResult(Result<IReadOnlyList<ProxySpec>>.Success(proxyRows)),
            () => faultRows,
            () => backupRows);
    }

    private sealed class FakeRegistry(IReadOnlyList<string> names) : ILabTargetRegistry
    {
        public IReadOnlyList<LabTarget> All { get; } = names
            .Select(name => new LabTarget("c:" + name, LabTargetKind.Container, name, null, T0))
            .ToArray();

        public Result<LabTarget> Register(string targetId, LabTargetKind kind, string name, string? containerId) =>
            throw new NotSupportedException();

        public LabTarget? Find(string targetId) => All.FirstOrDefault(target => target.TargetId == targetId);

        public void Remove(string targetId) => throw new NotSupportedException();

        public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class FakeDocker(string stdout) : IDockerCli
    {
        public IReadOnlyList<string> Args { get; private set; } = [];

        public Task<Result<DockerOutput>> RunAsync(DockerEngine engine, IReadOnlyList<string> args, CancellationToken ct)
        {
            Args = args;
            return Task.FromResult(Result<DockerOutput>.Success(new DockerOutput(0, stdout, string.Empty)));
        }
    }
}
