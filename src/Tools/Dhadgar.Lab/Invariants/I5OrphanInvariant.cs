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
public sealed class I5OrphanInvariant : ISkippableInvariant
{
    /// <summary>The docker label every lab container carries.</summary>
    public const string LabelFilter = "label=meridian-lab=1";

    /// <summary>The docker label every mutant image and container carries, with its id as the value (D-ML51, L54).</summary>
    public const string MutantLabelFilter = "label=meridian-lab-mutant";

    private const string ProxyDisableKind = "proxy_disable";

    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<string>>>> _containers;
    private readonly ILabTargetRegistry _registry;
    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<BrokerQueueInfo>>>> _queues;
    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<ProxySpec>>>> _proxies;
    private readonly Func<IReadOnlyList<ActiveFault>> _activeFaults;
    private readonly Func<IReadOnlyList<string>> _backupFolders;
    private readonly Func<bool>? _stackCreated;
    private readonly Func<CancellationToken, Task<Result<IReadOnlyList<string>>>>? _mutantContainers;
    private readonly Func<bool>? _mutationRunning;

    /// <summary>
    /// Creates the check over the five sources. <paramref name="stackCreated"/> says whether stack_up has created the lab
    /// credentials; while it is false the queues and proxies do not exist yet and are skipped (D-ML80). Null means always created.
    /// <paramref name="mutantContainers"/> lists the containers with the mutant label; each one is a violation while
    /// <paramref name="mutationRunning"/> says no <c>mutation</c> job runs (L54). Null for either leaves that source out.
    /// </summary>
    public I5OrphanInvariant(
        Func<CancellationToken, Task<Result<IReadOnlyList<string>>>> containers,
        ILabTargetRegistry registry,
        Func<CancellationToken, Task<Result<IReadOnlyList<BrokerQueueInfo>>>> queues,
        Func<CancellationToken, Task<Result<IReadOnlyList<ProxySpec>>>> proxies,
        Func<IReadOnlyList<ActiveFault>> activeFaults,
        Func<IReadOnlyList<string>> backupFolders,
        Func<bool>? stackCreated = null,
        Func<CancellationToken, Task<Result<IReadOnlyList<string>>>>? mutantContainers = null,
        Func<bool>? mutationRunning = null)
    {
        _mutantContainers = mutantContainers;
        _mutationRunning = mutationRunning;
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _queues = queues ?? throw new ArgumentNullException(nameof(queues));
        _proxies = proxies ?? throw new ArgumentNullException(nameof(proxies));
        _activeFaults = activeFaults ?? throw new ArgumentNullException(nameof(activeFaults));
        _backupFolders = backupFolders ?? throw new ArgumentNullException(nameof(backupFolders));
        _stackCreated = stackCreated;
    }

    /// <inheritdoc />
    public string Id => "I5";

    /// <summary>
    /// Lists the names of every container that carries the lab label, through <c>docker ps -a</c> on the given engine.
    /// </summary>
    public static Task<Result<IReadOnlyList<string>>> ListLabelledContainersAsync(IDockerCli docker, DockerEngine engine, CancellationToken ct) =>
        ListByLabelsAsync(docker, engine, [LabelFilter], ct);

    /// <summary>
    /// Lists the names of every container that carries both the lab label and the mutant label (L54, A2). Found by label only,
    /// never by a name pattern.
    /// </summary>
    public static Task<Result<IReadOnlyList<string>>> ListMutantContainersAsync(IDockerCli docker, DockerEngine engine, CancellationToken ct) =>
        ListByLabelsAsync(docker, engine, [LabelFilter, MutantLabelFilter], ct);

    private static async Task<Result<IReadOnlyList<string>>> ListByLabelsAsync(IDockerCli docker, DockerEngine engine, string[] labelFilters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docker);

        var args = new List<string> { "ps", "-a" };
        foreach (string filter in labelFilters)
        {
            args.Add("--filter");
            args.Add(filter);
        }

        args.Add("--format");
        args.Add("{{.Names}}");
        Result<DockerOutput> run = await docker.RunAsync(engine, args, ct).ConfigureAwait(false);
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
        Result<InvariantOutcome> outcome = await CheckWithSkipsAsync(window, ct).ConfigureAwait(false);
        return outcome.IsSuccess
            ? Result<IReadOnlyList<InvariantViolation>>.Success(outcome.Value.Violations)
            : Result<IReadOnlyList<InvariantViolation>>.Failure(outcome.Error);
    }

    /// <inheritdoc />
    public async Task<Result<InvariantOutcome>> CheckWithSkipsAsync(InvariantWindow window, CancellationToken ct)
    {
        var violations = new List<InvariantViolation>();
        var skipped = new List<InvariantSkip>();

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

        if (_mutantContainers is not null)
        {
            // L54 (A2): a mutant container is found by its label; it may exist only while a mutation job runs.
            Result<IReadOnlyList<string>> mutants = await _mutantContainers(ct).ConfigureAwait(false);
            if (mutants.IsFailure)
            {
                return Failed("mutant containers", mutants.Error);
            }

            bool mutationJob = _mutationRunning?.Invoke() ?? false;
            if (!mutationJob)
            {
                foreach (string name in mutants.Value)
                {
                    violations.Add(Violation(name, "mutant-outside-job", $"mutant container {name} is labelled meridian-lab-mutant while no mutation job runs", window));
                }
            }
        }

        IReadOnlyList<ActiveFault> active = _activeFaults();
        bool stack = _stackCreated?.Invoke() ?? true;
        if (!stack)
        {
            // D-ML80: the queues and proxies belong to the stack that stack_up creates; before that they are not applicable.
            skipped.Add(new InvariantSkip(Id, "queues", I2OutboxInvariant.SkipReason(DbApplicability.StackNotUp)));
            skipped.Add(new InvariantSkip(Id, "proxies", I2OutboxInvariant.SkipReason(DbApplicability.StackNotUp)));
        }
        else
        {
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
        }

        var activeIds = active.Select(fault => fault.FaultId).ToHashSet(StringComparer.Ordinal);
        foreach (string folder in _backupFolders())
        {
            if (!activeIds.Contains(folder))
            {
                violations.Add(Violation(folder, "orphan-fault-backup", $"fault backup {folder} has no active fault", window));
            }
        }

        return Result<InvariantOutcome>.Success(new InvariantOutcome(violations, skipped));
    }

    private InvariantViolation Violation(string service, string checkId, string detail, InvariantWindow window) =>
        new(Id, service, checkId, detail.Length > 512 ? detail[..512] : detail, window.To, null);

    private static Result<InvariantOutcome> Failed(string source, string error) =>
        Result<InvariantOutcome>.Failure($"{source}: {error}");
}
