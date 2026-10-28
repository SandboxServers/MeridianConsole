using System.Globalization;
using System.Text.Json;
using Dhadgar.Lab.Docker;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Faults.Kinds;

/// <summary>
/// Arguments of the <c>disk_fill</c> fault kind: the declared mount (must be <c>/tmp</c>, C10) and the KiB to leave free (default 64).
/// </summary>
public sealed record DiskFillArgs(string? Mount = null, int? LeaveKib = null);

/// <summary>
/// Heal data of a disk fill: the container, the free space seen before the fill, and the KiB to leave free.
/// </summary>
public sealed record DiskFillHealData(string ContainerId, int AvailableKib, int LeaveKib);

/// <summary>
/// The <c>disk_fill</c> fault kind (<c>fault_disk_fill</c>): fills <c>/tmp</c> inside one Linux service container, leaving
/// <c>leave_kib</c> free. Heal removes the fill file. Every docker call is a separate <c>ArgumentList</c> item (R-ARGS).
/// </summary>
public sealed class DiskFillFault(
    IDockerCli docker,
    ILabTargetRegistry registry,
    FaultTargetGuard guard,
    ILogger<DiskFillFault> logger) : IFaultKind
{
    internal const string DeclaredMount = "/tmp";
    internal const string FillFile = "/tmp/.lab-fill";
    private const int DefaultLeaveKib = 64;

    /// <summary>The largest tmpfs <c>/tmp</c> that disk_fill will fill (D-ML82): 256 MiB.</summary>
    internal const int MaxTmpfsKib = 262144;

    private readonly IDockerCli _docker = docker ?? throw new ArgumentNullException(nameof(docker));
    private readonly ILabTargetRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly FaultTargetGuard _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    private readonly ILogger<DiskFillFault> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string Kind => "disk_fill";

    /// <inheritdoc />
    public async Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<DiskFillArgs> parsed = KindSupport.FromHealData<DiskFillArgs>(args);
        if (parsed.IsFailure)
        {
            return Result<JsonElement>.Failure(parsed.Error);
        }

        // The mount is refused before any docker call unless it is the declared one (C10).
        if (!string.Equals(parsed.Value.Mount, DeclaredMount, StringComparison.Ordinal))
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] mount must be /tmp, the declared disk-fill mount");
        }

        int leave = parsed.Value.LeaveKib ?? DefaultLeaveKib;
        if (leave < 0)
        {
            return Result<JsonElement>.Failure("[Lab.InvalidArgument] leave_kib must be zero or more");
        }

        Result<string> containerId = ContainerSupport.ContainerIdOf(target);
        if (containerId.IsFailure)
        {
            return Result<JsonElement>.Failure(containerId.Error);
        }

        Result<DockerOutput> df = await ContainerSupport.RunAsync(
            _docker, ct, "exec", containerId.Value, "df", "-k", "--output=fstype,size,avail", DeclaredMount).ConfigureAwait(false);
        if (df.IsFailure || df.Value.ExitCode != 0)
        {
            return Result<JsonElement>.Failure("[Lab.Docker] free space of /tmp could not be read");
        }

        // A header line, then "<fstype> <size KiB> <avail KiB>".
        string last = df.Value.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        string[] columns = last.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length != 3
            || !long.TryParse(columns[1], NumberStyles.None, CultureInfo.InvariantCulture, out long size)
            || !int.TryParse(columns[2], NumberStyles.None, CultureInfo.InvariantCulture, out int avail))
        {
            return Result<JsonElement>.Failure("[Lab.Docker] free space output is unreadable");
        }

        // D-ML82: only a bounded tmpfs can be filled; an overlay /tmp is the Docker VM disk that the dev stack shares.
        if (!string.Equals(columns[0], "tmpfs", StringComparison.Ordinal) || size > MaxTmpfsKib)
        {
            return Result<JsonElement>.Failure("[Lab.BlastRadius] /tmp is not a bounded tmpfs");
        }

        int available = (int)Math.Min(avail, size);

        if (available <= leave)
        {
            return Result<JsonElement>.Failure("[Lab.Docker] there is no free space above leave_kib");
        }

        return KindSupport.ToHealData(new DiskFillHealData(containerId.Value, available, leave));
    }

    /// <inheritdoc />
    public async Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        Result<DiskFillHealData> heal = KindSupport.FromHealData<DiskFillHealData>(healData);
        if (heal.IsFailure)
        {
            return Result.Failure(heal.Error);
        }

        int kib = heal.Value.AvailableKib - heal.Value.LeaveKib;
        if (kib <= 0)
        {
            return Result.Failure("[Lab.Docker] there is nothing to fill above leave_kib");
        }

        string size = kib.ToString(CultureInfo.InvariantCulture);
        Result<DockerOutput> fallocate = await ContainerSupport.RunAsync(
            _docker, ct, "exec", heal.Value.ContainerId, "fallocate", "-l", size + "K", FillFile).ConfigureAwait(false);
        if (fallocate.IsSuccess && fallocate.Value.ExitCode == 0)
        {
            return Result.Success();
        }

        // fallocate is not on every image; dd writes the same KiB from /dev/zero.
        Result<DockerOutput> dd = await ContainerSupport.RunAsync(
            _docker, ct, "exec", heal.Value.ContainerId, "dd", "if=/dev/zero", "of=" + FillFile, "bs=1024", "count=" + size).ConfigureAwait(false);
        return dd.IsFailure || dd.Value.ExitCode != 0
            ? Result.Failure("[Lab.Docker] the disk could not be filled")
            : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Result<DiskFillHealData> heal = KindSupport.FromHealData<DiskFillHealData>(entry.HealData);
        if (heal.IsFailure)
        {
            return Result.Success();
        }

        Result owned = await ContainerSupport.CheckHealRefAsync(_docker, heal.Value.ContainerId, ct).ConfigureAwait(false);
        if (owned.IsFailure)
        {
            return owned;
        }

        return await RemoveFillAsync(heal.Value.ContainerId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<int>> ReconcileAsync(CancellationToken ct)
    {
        int removed = 0;
        foreach (LabTarget target in _registry.All.Where(t => t.Kind == LabTargetKind.Container && !string.IsNullOrEmpty(t.ContainerId)))
        {
            // Every candidate goes through the blast-radius guard first; a non-lab container is never touched.
            Result<ResolvedTarget> resolved = await _guard.ResolveAsync(target.TargetId, Kind, ct).ConfigureAwait(false);
            if (resolved.IsFailure)
            {
                continue;
            }

            Result<DockerOutput> inspect = await ContainerSupport.RunAsync(
                _docker, ct, "inspect", "--format", "{{.State.Status}}", resolved.Value.ContainerId!).ConfigureAwait(false);
            if (inspect.IsFailure || inspect.Value.ExitCode != 0 || inspect.Value.StdOut.Trim() != "running")
            {
                continue;
            }

            Result removedNow = await RemoveFillAsync(resolved.Value.ContainerId!, ct).ConfigureAwait(false);
            if (removedNow.IsSuccess)
            {
                removed++;
                _logger.LogInformation("Reconcile removed the disk fill file in {Target}", target.TargetId);
            }
        }

        return Result<int>.Success(removed);
    }

    private async Task<Result> RemoveFillAsync(string containerId, CancellationToken ct)
    {
        Result<DockerOutput> run = await ContainerSupport.RunAsync(
            _docker, ct, "exec", containerId, "rm", "-f", FillFile).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return Result.Failure(run.Error);
        }

        // rm -f is quiet when the file is absent; a stopped container has no fill to remove, so heal is done too.
        if (run.Value.ExitCode == 0 || ContainerSupport.SaysAlreadyDone(run.Value, "is not running", "No such container"))
        {
            return Result.Success();
        }

        return Result.Failure("[Lab.Docker] the fill file could not be removed");
    }
}
