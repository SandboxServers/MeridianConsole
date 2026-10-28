using System.Globalization;
using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// Arguments of the <c>kill</c> fault kind: the signal (<c>KILL</c> or <c>TERM</c>, default <c>KILL</c>).
/// </summary>
public sealed record KillArgs(string? Signal = null);

/// <summary>
/// Heal data of a kill: the container, the signal, and the restart count and start time seen before the kill.
/// </summary>
public sealed record KillHealData(string ContainerId, string Signal, int RestartCount, string StartedAt);

/// <summary>
/// The <c>kill</c> fault kind (<c>fault_kill</c>): <c>docker kill --signal</c> on one lab container. Heal starts it again.
/// The watchdog does not relaunch a container while the fault is active (<see cref="FaultBook.IsFaulted"/>).
/// </summary>
public sealed class KillFault(
    IDockerCli docker,
    ILabTargetRegistry registry,
    FaultTargetGuard guard,
    IFaultJournal journal,
    ILogger<KillFault> logger) : IFaultKind
{
    private static readonly HashSet<string> Signals = new(StringComparer.Ordinal) { "KILL", "TERM" };

    private readonly IDockerCli _docker = docker ?? throw new ArgumentNullException(nameof(docker));
    private readonly ILabTargetRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly FaultTargetGuard _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    private readonly IFaultJournal _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    private readonly ILogger<KillFault> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string Kind => "kill";

    /// <inheritdoc />
    public async Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<KillArgs> parsed = KindSupport.FromHealData<KillArgs>(args);
        if (parsed.IsFailure)
        {
            return Result<JsonElement>.Failure(parsed.Error);
        }

        string signal = parsed.Value.Signal ?? "KILL";
        if (!Signals.Contains(signal))
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] signal must be KILL or TERM");
        }

        Result<string> containerId = ContainerSupport.ContainerIdOf(target);
        if (containerId.IsFailure)
        {
            return Result<JsonElement>.Failure(containerId.Error);
        }

        Result<DockerOutput> run = await ContainerSupport.RunAsync(
            _docker, ct, "inspect", "--format", "{{.RestartCount}}|{{.State.StartedAt}}", containerId.Value).ConfigureAwait(false);
        if (run.IsFailure || run.Value.ExitCode != 0)
        {
            return Result<JsonElement>.Failure("[Lab.Docker] container could not be inspected before the kill");
        }

        string[] parts = run.Value.StdOut.Trim().Split('|');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int restarts))
        {
            return Result<JsonElement>.Failure("[Lab.Docker] container inspect output is unreadable");
        }

        return KindSupport.ToHealData(new KillHealData(containerId.Value, signal, restarts, parts[1]));
    }

    /// <inheritdoc />
    public async Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<KillHealData> heal = KindSupport.FromHealData<KillHealData>(healData);
        if (heal.IsFailure)
        {
            return Result.Failure(heal.Error);
        }

        Result<DockerOutput> run = await ContainerSupport.RunAsync(
            _docker, ct, "kill", "--signal", heal.Value.Signal, heal.Value.ContainerId).ConfigureAwait(false);
        return run.IsFailure || run.Value.ExitCode != 0
            ? Result.Failure("[Lab.Docker] container could not be killed")
            : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Result<KillHealData> heal = KindSupport.FromHealData<KillHealData>(entry.HealData);
        if (heal.IsFailure)
        {
            return Result.Success();
        }

        Result owned = await ContainerSupport.CheckHealRefAsync(_docker, heal.Value.ContainerId, ct).ConfigureAwait(false);
        if (owned.IsFailure)
        {
            return owned;
        }

        return await StartAsync(heal.Value.ContainerId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReconcileAsync(CancellationToken ct)
    {
        Result<IReadOnlyList<FaultJournalEntry>> journalled = await _journal.ReadAsync(ct).ConfigureAwait(false);
        if (journalled.IsFailure)
        {
            return Result<int>.Failure(journalled.Error);
        }

        int started = 0;
        foreach (LabTarget target in _registry.All.Where(t => t.Kind == LabTargetKind.Container && !string.IsNullOrEmpty(t.ContainerId)))
        {
            // Every candidate goes through the blast-radius guard first; a non-lab container is never touched.
            Result<ResolvedTarget> resolved = await _guard.ResolveAsync(target.TargetId, Kind, ct).ConfigureAwait(false);
            if (resolved.IsFailure || journalled.Value.Any(e => string.Equals(e.TargetId, target.TargetId, StringComparison.Ordinal)))
            {
                continue;
            }

            // Only a container stopped by a signal (137 KILL, 143 TERM) and owned by no journal entry is an orphan of this kind.
            Result<DockerOutput> inspect = await ContainerSupport.RunAsync(
                _docker, ct, "inspect", "--format", "{{.State.Status}}|{{.State.ExitCode}}", resolved.Value.ContainerId!).ConfigureAwait(false);
            if (inspect.IsFailure || inspect.Value.ExitCode != 0)
            {
                continue;
            }

            string[] parts = inspect.Value.StdOut.Trim().Split('|');
            bool signalled = parts.Length == 2 && parts[0] == "exited" && (parts[1] == "137" || parts[1] == "143");
            if (!signalled)
            {
                continue;
            }

            Result startedNow = await StartAsync(resolved.Value.ContainerId!, ct).ConfigureAwait(false);
            if (startedNow.IsSuccess)
            {
                started++;
                _logger.LogInformation("Reconcile started {Target}, an unowned killed container", target.TargetId);
            }
        }

        return Result<int>.Success(started);
    }

    private async Task<Result> StartAsync(string containerId, CancellationToken ct)
    {
        Result<DockerOutput> run = await ContainerSupport.RunAsync(_docker, ct, "start", containerId).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result.Failure(run.Error);
        }

        // "already running" and "no such container" both mean there is nothing to start: heal is done.
        if (run.Value.ExitCode == 0 || ContainerSupport.SaysAlreadyDone(run.Value, "already", "No such container"))
        {
            return Result.Success();
        }

        return Result.Failure("[Lab.Docker] container could not be started");
    }
}
