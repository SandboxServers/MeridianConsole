using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// The <c>pause</c> fault kind (<c>fault_pause</c>): <c>docker pause</c> on one lab container; heal is <c>docker unpause</c>.
/// </summary>
public sealed class PauseFault(
    IDockerCli docker,
    ILabTargetRegistry registry,
    FaultTargetGuard guard,
    ILogger<PauseFault> logger) : IFaultKind
{
    private readonly IDockerCli _docker = docker ?? throw new ArgumentNullException(nameof(docker));
    private readonly ILabTargetRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly FaultTargetGuard _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    private readonly ILogger<PauseFault> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string Kind => "pause";

    /// <inheritdoc />
    public Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<string> containerId = ContainerSupport.ContainerIdOf(target);
        return Task.FromResult(containerId.IsFailure
            ? Result<JsonElement>.Failure(containerId.Error)
            : KindSupport.ToHealData(new ContainerHealData(containerId.Value)));
    }

    /// <inheritdoc />
    public async Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<ContainerHealData> heal = KindSupport.FromHealData<ContainerHealData>(healData);
        if (heal.IsFailure)
        {
            return Result.Failure(heal.Error);
        }

        Result<DockerOutput> run = await ContainerSupport.RunAsync(_docker, ct, "pause", heal.Value.ContainerId).ConfigureAwait(false);
        return run.IsFailure || run.Value.ExitCode != 0
            ? Result.Failure("[Lab.Docker] container could not be paused")
            : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Result<ContainerHealData> heal = KindSupport.FromHealData<ContainerHealData>(entry.HealData);
        if (heal.IsFailure)
        {
            return Result.Success();
        }

        Result owned = await ContainerSupport.CheckHealRefAsync(_docker, heal.Value.ContainerId, ct).ConfigureAwait(false);
        if (owned.IsFailure)
        {
            return owned;
        }

        return await UnpauseAsync(heal.Value.ContainerId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReconcileAsync(CancellationToken ct)
    {
        int unpaused = 0;
        foreach (LabTarget target in _registry.All.Where(t => t.Kind == LabTargetKind.Container && !string.IsNullOrEmpty(t.ContainerId)))
        {
            // Every candidate goes through the blast-radius guard; a non-lab container is never touched.
            Result<ResolvedTarget> resolved = await _guard.ResolveAsync(target.TargetId, Kind, ct).ConfigureAwait(false);
            if (resolved.IsFailure)
            {
                continue;
            }

            Result<DockerOutput> inspect = await ContainerSupport.RunAsync(
                _docker, ct, "inspect", "--format", "{{.State.Paused}}", resolved.Value.ContainerId!).ConfigureAwait(false);
            if (inspect.IsFailure || inspect.Value.ExitCode != 0 || inspect.Value.StdOut.Trim() != "true")
            {
                continue;
            }

            Result done = await UnpauseAsync(resolved.Value.ContainerId!, ct).ConfigureAwait(false);
            if (done.IsSuccess)
            {
                unpaused++;
                _logger.LogInformation("Reconcile unpaused {Target}, an unowned paused container", target.TargetId);
            }
        }

        return Result<int>.Success(unpaused);
    }

    private async Task<Result> UnpauseAsync(string containerId, CancellationToken ct)
    {
        Result<DockerOutput> run = await ContainerSupport.RunAsync(_docker, ct, "unpause", containerId).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result.Failure(run.Error);
        }

        // "is not paused" means the heal is already done.
        return run.Value.ExitCode == 0 || ContainerSupport.SaysAlreadyDone(run.Value, "is not paused")
            ? Result.Success()
            : Result.Failure("[Lab.Docker] container could not be unpaused");
    }
}
