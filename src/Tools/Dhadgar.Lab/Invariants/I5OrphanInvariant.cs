using Dhadgar.Lab.Docker;
using Dhadgar.Lab.Faults;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// One RabbitMQ queue as the management API reports it: the name and the ready message count.
/// </summary>
public sealed record BrokerQueueInfo(string Name, long Messages);

/// <summary>
/// I5: no orphans (C11). Four sources are compared with what the lab knows:
/// labelled containers with no registry target, <c>_error</c> or <c>_skipped</c> queues with messages, disabled Toxiproxy
/// proxies with no active proxy fault in <c>FaultBook</c>, and fault backup folders with no active fault.
/// Each source is a delegate so the check runs on fakes; the real wiring is in <c>InvariantsArea</c> (L21 step 4).
/// </summary>
public sealed class I5OrphanInvariant : IInvariant
{
    /// <summary>The docker label every lab container carries.</summary>
    public const string LabelFilter = "label=meridian-lab=1";

    private const string ProxyDisableKind = "proxy_disable";

    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<string>>>> _containers;
    private readonly ILabTargetRegistry _registry;
    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<BrokerQueueInfo>>>> _queues;
    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<ProxySpec>>>> _proxies;
    private readonly Func<IReadOnlyList<ActiveFault>> _activeFaults;
    private readonly Func<IReadOnlyList<string>> _backupFolders;

    /// <summary>
    /// Creates the check over the five sources.
    /// </summary>
    public I5OrphanInvariant(
        Func<CancellationToken, Task<Result<IReadOnlyList<string>>>> containers,
        ILabTargetRegistry registry,
        Func<CancellationToken, Task<Result<IReadOnlyList<BrokerQueueInfo>>>> queues,
        Func<CancellationToken, Task<Result<IReadOnlyList<ProxySpec>>>> proxies,
        Func<IReadOnlyList<ActiveFault>> activeFaults,
        Func<IReadOnlyList<string>> backupFolders)
    {
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _queues = queues ?? throw new ArgumentNullException(nameof(queues));
        _proxies = proxies ?? throw new ArgumentNullException(nameof(proxies));
        _activeFaults = activeFaults ?? throw new ArgumentNullException(nameof(activeFaults));
        _backupFolders = backupFolders ?? throw new ArgumentNullException(nameof(backupFolders));
    }

    /// <inheritdoc />
    public string Id => "I5";

    /// <summary>
    /// Lists the names of every container that carries the lab label, through <c>docker ps -a</c> on the given engine.
    /// </summary>
    public static async Task<Result<IReadOnlyList<string>>> ListLabelledContainersAsync(IDockerCli docker, DockerEngine engine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docker);

        Result<DockerOutput> run = await docker.RunAsync(
            engine,
            ["ps", "-a", "--filter", LabelFilter, "--format", "{{.Names}}"],
            ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result<IReadOnlyList<string>>.Failure(run.Error);
        }

        if (run.Value.ExitCode != 0)
        {
            return Result<IReadOnlyList<string>>.Failure($"[Lab.Docker] docker ps exited {run.Value.ExitCode}");
        }

        IReadOnlyList<string> names = run.Value.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        return Result<IReadOnlyList<string>>.Success(names);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct)
    {
        var violations = new List<InvariantViolation>();

        Result<IReadOnlyList<string>> containers = await _containers(ct).ConfigureAwait(false);
        if (containers.IsFailure)
        {
            return Failed("containers", containers.Error);
        }

        IReadOnlyList<LabTarget> targets = _registry.All;
        foreach (string name in containers.Value)
        {
            if (!targets.Any(target => string.Equals(target.Name, name, StringComparison.Ordinal)))
            {
                violations.Add(Violation(name, "orphan-container", $"labelled container {name} is not in the target registry", window));
            }
        }

        Result<IReadOnlyList<BrokerQueueInfo>> queues = await _queues(ct).ConfigureAwait(false);
        if (queues.IsFailure)
        {
            return Failed("queues", queues.Error);
        }

        foreach (BrokerQueueInfo queue in queues.Value)
        {
            bool failureQueue = queue.Name.EndsWith("_error", StringComparison.Ordinal) || queue.Name.EndsWith("_skipped", StringComparison.Ordinal);
            if (failureQueue && queue.Messages > 0)
            {
                violations.Add(Violation(queue.Name, "queue-backlog", $"queue {queue.Name} holds {queue.Messages} message(s)", window));
            }
        }

        IReadOnlyList<ActiveFault> active = _activeFaults();
        Result<IReadOnlyList<ProxySpec>> proxies = await _proxies(ct).ConfigureAwait(false);
        if (proxies.IsFailure)
        {
            return Failed("proxies", proxies.Error);
        }

        bool proxyFaultActive = active.Any(fault => fault.Kind == ProxyDisableKind);
        foreach (ProxySpec proxy in proxies.Value)
        {
            if (!proxy.Enabled && !proxyFaultActive)
            {
                violations.Add(Violation(proxy.Name, "disabled-proxy-unexplained", $"proxy {proxy.Name} is disabled with no active proxy fault", window));
            }
        }

        var activeIds = active.Select(fault => fault.FaultId).ToHashSet(StringComparer.Ordinal);
        foreach (string folder in _backupFolders())
        {
            if (!activeIds.Contains(folder))
            {
                violations.Add(Violation(folder, "orphan-fault-backup", $"fault backup {folder} has no active fault", window));
            }
        }

        return Result<IReadOnlyList<InvariantViolation>>.Success(violations);
    }

    private InvariantViolation Violation(string service, string checkId, string detail, InvariantWindow window) =>
        new(Id, service, checkId, detail.Length > 512 ? detail[..512] : detail, window.To, null);

    private static Result<IReadOnlyList<InvariantViolation>> Failed(string source, string error) =>
        Result<IReadOnlyList<InvariantViolation>>.Failure($"{source}: {error}");
}
