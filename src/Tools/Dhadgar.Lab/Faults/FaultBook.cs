using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Processes;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Lab.Faults;

/// <summary>
/// One fault type (<c>toxic</c>, <c>partition</c>, ...). Prepare computes everything heal needs without changing the target;
/// inject changes it; heal reverses it and is safe when the fault was never injected or only partly injected.
/// </summary>
public interface IFaultKind
{
    /// <summary>
    /// The fault kind name used in tool arguments and the journal.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Computes everything heal needs (names, prior values, backups written to <c>LabPaths.FaultBackups\&lt;faultId&gt;\</c>)
    /// without changing the target.
    /// </summary>
    Task<Result<JsonElement>> PrepareAsync(ResolvedTarget target, JsonElement args, string faultId, CancellationToken ct);

    /// <summary>
    /// Changes the target, using the heal data from prepare.
    /// </summary>
    Task<Result> InjectAsync(ResolvedTarget target, JsonElement args, JsonElement healData, string faultId, CancellationToken ct);

    /// <summary>
    /// Idempotent, and safe when the fault was never injected or only partly injected ("not paused", "already running",
    /// "no such toxic", "file absent" are ok).
    /// </summary>
    Task<Result> HealAsync(FaultJournalEntry entry, CancellationToken ct);

    /// <summary>
    /// Daemon start only, after journalled entries are healed: removes this kind's traces that no journal entry owns.
    /// Lab targets only, through the guard.
    /// </summary>
    Task<Result<int>> ReconcileAsync(CancellationToken ct);
}

/// <summary>
/// A fault that is in place (or being healed), as reported by <c>faults_active</c> and <c>StatusDto.ActiveFaults</c>.
/// </summary>
public sealed record ActiveFault(
    string FaultId,
    string Kind,
    string TargetId,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    FaultState State,
    string? HealError);

/// <summary>
/// One line of the in-memory heal history (last 500 kept): what healed, when, and why.
/// </summary>
public sealed record FaultHealRecord(string FaultId, string Kind, string TargetId, string Reason, DateTimeOffset HealedAt, bool Healed);

/// <summary>
/// The fault book: write-ahead journal, deadlines, recovery and lease-end healing (L19). A singleton and a hosted service.
/// Every mutating call runs under one gate, so the journal is written in the order the changes happen.
/// </summary>
public sealed class FaultBook : IFaultTargets, ILeaseEndHook, ILabStatusSource, IHostedService, IDisposable
{
    /// <summary>
    /// Most faults that may be journalled at once.
    /// </summary>
    public const int MaxActiveFaults = 16;

    /// <summary>
    /// Heal records kept in memory.
    /// </summary>
    public const int MaxHistory = 500;

    /// <summary>
    /// Lowest allowed <c>heal_after_s</c>.
    /// </summary>
    public const int MinHealAfterS = 1;

    /// <summary>
    /// Highest allowed <c>heal_after_s</c>.
    /// </summary>
    public const int MaxHealAfterS = 900;

    /// <summary>
    /// How long a target still counts as faulted after its last heal.
    /// </summary>
    public static readonly TimeSpan RecentHealWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often the deadline sweeper runs.
    /// </summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    private readonly FaultTargetGuard _guard;
    private readonly IFaultJournal _journal;
    private readonly Dictionary<string, IFaultKind> _kinds;
    private readonly TimeProvider _time;
    private readonly ILogger<FaultBook> _logger;
    private readonly ILabObservationSink? _observations;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _sweepGate = new(1, 1);
    private readonly object _state = new();
    private readonly Dictionary<string, FaultJournalEntry> _entries = new(StringComparer.Ordinal);
    private readonly List<FaultHealRecord> _history = [];
    private ITimer? _sweeper;
    private string? _runId;

    /// <summary>
    /// Creates the fault book over the guard, the journal and the registered fault kinds.
    /// </summary>
    public FaultBook(
        FaultTargetGuard guard,
        IFaultJournal journal,
        IEnumerable<IFaultKind> kinds,
        TimeProvider time,
        ILogger<FaultBook> logger,
        ILabObservationSink? observations = null)
    {
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        ArgumentNullException.ThrowIfNull(kinds);
        _kinds = kinds.ToDictionary(kind => kind.Kind, StringComparer.Ordinal);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _observations = observations;
    }

    /// <summary>
    /// Sets the observation run that fault lines are written to. Null stops writing them.
    /// </summary>
    public void BindRun(string? runId)
    {
        lock (_state)
        {
            _runId = runId;
        }
    }

    /// <summary>
    /// Starts a fault: guard, checks, prepare, write-ahead journal, inject. See the packet for the order.
    /// </summary>
    public async Task<Result<ActiveFault>> BeginAsync(string kind, string targetId, JsonElement args, int healAfterS, CancellationToken ct)
    {
        if (healAfterS is < MinHealAfterS or > MaxHealAfterS)
        {
            return Result<ActiveFault>.Failure("[Lab.InvalidArgument] heal_after_s must be 1..900");
        }

        if (!_kinds.TryGetValue(kind, out IFaultKind? faultKind))
        {
            return Result<ActiveFault>.Failure("[Lab.InvalidArgument] unknown fault kind");
        }

        var target = await _guard.ResolveAsync(targetId, kind, ct).ConfigureAwait(false);
        if (target.IsFailure)
        {
            return Result<ActiveFault>.Failure(target.Error);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? clash;
            int count;
            lock (_state)
            {
                clash = _entries.Values.FirstOrDefault(e => e.Kind == kind && e.TargetId == targetId)?.FaultId;
                count = _entries.Count;
            }

            if (clash is not null)
            {
                return Result<ActiveFault>.Failure($"[Lab.FaultActive] {clash}");
            }

            if (count >= MaxActiveFaults)
            {
                return Result<ActiveFault>.Failure("[Lab.FaultActive] too many active faults");
            }

            string faultId = NewFaultId();
            Result<JsonElement> prepared = await RunKindAsync(() => faultKind.PrepareAsync(target.Value, args, faultId, ct)).ConfigureAwait(false);
            if (prepared.IsFailure)
            {
                return Result<ActiveFault>.Failure(prepared.Error);
            }

            DateTimeOffset now = _time.GetUtcNow();
            var entry = new FaultJournalEntry(faultId, kind, targetId, args, prepared.Value, FaultState.Prepared, now, now.AddSeconds(healAfterS), null);
            Replace(entry);

            // Write-ahead (D-ML46): the entry is durable before anything changes the target.
            Result journaled = await PersistAsync(ct).ConfigureAwait(false);
            if (journaled.IsFailure)
            {
                Remove(faultId);
                return Result<ActiveFault>.Failure(journaled.Error);
            }

            Observe(ObservationKind.FaultPrepare, entry, "prepared");

            Result injected = await RunKindAsync(() => faultKind.InjectAsync(target.Value, args, prepared.Value, faultId, ct)).ConfigureAwait(false);
            if (injected.IsFailure)
            {
                // A partial inject is healed now; if that fails the entry stays journalled as HealFailed.
                await HealLockedAsync(faultId, "inject_failed", ct).ConfigureAwait(false);
                return Result<ActiveFault>.Failure(injected.Error);
            }

            FaultJournalEntry active = entry with { State = FaultState.Active };
            Replace(active);
            Result activated = await PersistAsync(ct).ConfigureAwait(false);
            if (activated.IsFailure)
            {
                // The journal on disk still says Prepared, so recovery heals it; the sweeper heals it at the deadline.
                return Result<ActiveFault>.Failure(activated.Error);
            }

            Observe(ObservationKind.FaultInject, active, "active");
            return Result<ActiveFault>.Success(ToActive(active));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Heals one journalled fault. Fails with <c>[Lab.NotFound]</c> when the id is not journalled.
    /// </summary>
    public async Task<Result> HealAsync(string faultId, string reason, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await HealLockedAsync(faultId, reason, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Heals every journalled fault. Returns the number healed, or a failure when any heal failed.
    /// </summary>
    public async Task<Result<int>> HealAllAsync(string reason, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string[] ids;
            lock (_state)
            {
                ids = _entries.Keys.ToArray();
            }

            int healed = 0;
            int failed = 0;
            foreach (string id in ids)
            {
                if ((await HealLockedAsync(id, reason, ct).ConfigureAwait(false)).IsSuccess)
                {
                    healed++;
                }
                else
                {
                    failed++;
                }
            }

            return failed > 0
                ? Result<int>.Failure($"[Lab.FaultHeal] {failed} fault(s) could not be healed; see faults_active")
                : Result<int>.Success(healed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The journalled faults, oldest first.
    /// </summary>
    public IReadOnlyList<ActiveFault> ListActive()
    {
        lock (_state)
        {
            return _entries.Values.OrderBy(e => e.StartedAt).Select(ToActive).ToArray();
        }
    }

    /// <summary>
    /// The heal history, oldest first (at most <see cref="MaxHistory"/> lines).
    /// </summary>
    public IReadOnlyList<FaultHealRecord> History()
    {
        lock (_state)
        {
            return _history.ToArray();
        }
    }

    /// <inheritdoc />
    public bool IsFaulted(string targetId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_state)
        {
            if (_entries.Values.Any(e => e.TargetId == targetId))
            {
                return true;
            }

            return _history.Any(h => h.Healed && h.TargetId == targetId && now - h.HealedAt < RecentHealWindow);
        }
    }

    /// <inheritdoc />
    public async Task OnLeaseEndedAsync(LeaseHistoryEntry entry, CancellationToken ct)
    {
        // The lease itself is not needed: every fault goes, whoever held the lease.
        Result<int> healed = await HealAllAsync("lease_ended", ct).ConfigureAwait(false);
        if (healed.IsFailure)
        {
            _logger.LogWarning("lab.faults lease end could not heal every fault");
        }
    }

    /// <inheritdoc />
    public void Contribute(StatusBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        lock (_state)
        {
            builder.ActiveFaults = _entries.Count;
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RecoverAsync(cancellationToken).ConfigureAwait(false);
        _sweeper = _time.CreateTimer(OnSweepTick, null, SweepInterval, SweepInterval);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _sweeper?.Dispose();
        _gate.Dispose();
        _sweepGate.Dispose();
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _sweeper?.Dispose();
        _sweeper = null;
        await HealAllAsync("shutdown", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Heals every journalled entry (any state), then runs every kind's reconcile, and logs the counts.
    /// </summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            int healed = 0;
            int failed = 0;
            Result<IReadOnlyList<FaultJournalEntry>> read = await ReadJournalAsync(ct).ConfigureAwait(false);
            if (read.IsFailure)
            {
                _logger.LogWarning("lab.faults recovery could not read the journal; reconcile still runs");
            }
            else
            {
                lock (_state)
                {
                    _entries.Clear();
                    foreach (FaultJournalEntry entry in read.Value)
                    {
                        _entries[entry.FaultId] = entry;
                    }
                }

                string[] ids;
                lock (_state)
                {
                    ids = _entries.Keys.ToArray();
                }

                foreach (string id in ids)
                {
                    if ((await HealLockedAsync(id, "recovery", ct).ConfigureAwait(false)).IsSuccess)
                    {
                        healed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            }

            int reconciled = 0;
            foreach (IFaultKind kind in _kinds.Values)
            {
                Result<int> result = await RunKindAsync(() => kind.ReconcileAsync(ct)).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    reconciled += result.Value;
                }
                else
                {
                    failed++;
                }
            }

            _logger.LogInformation("lab.faults recovery healed {Healed} reconciled {Reconciled} failed {Failed}", healed, reconciled, failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnSweepTick(object? state)
    {
        // The timer cannot await; the sweep runs detached and reports through the logger.
        _ = SweepOnceAsync(CancellationToken.None);
    }

    /// <summary>
    /// Heals every entry whose deadline has passed. Skips the run when the previous sweep is still going.
    /// </summary>
    private async Task SweepOnceAsync(CancellationToken ct)
    {
        if (!_sweepGate.Wait(0, CancellationToken.None))
        {
            return;
        }

        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string[] overdue;
                lock (_state)
                {
                    overdue = _entries.Values.Where(e => e.Deadline <= now).Select(e => e.FaultId).ToArray();
                }

                foreach (string id in overdue)
                {
                    await HealLockedAsync(id, "deadline", ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("lab.faults sweep failed with {ExceptionType}", ex.GetType().Name);
        }
        finally
        {
            _sweepGate.Release();
        }
    }

    /// <summary>
    /// Heals one entry, retrying once. Caller holds the gate. On success the entry leaves the journal; on failure it stays
    /// journalled as <c>HealFailed</c> with the error.
    /// </summary>
    private async Task<Result> HealLockedAsync(string faultId, string reason, CancellationToken ct)
    {
        FaultJournalEntry? current;
        lock (_state)
        {
            current = _entries.GetValueOrDefault(faultId);
        }

        if (current is null)
        {
            return Result.Failure("[Lab.NotFound] fault is not journalled");
        }

        Replace(current with { State = FaultState.Healing });
        await PersistBestEffortAsync(ct).ConfigureAwait(false);

        Result outcome = await HealWithRetryAsync(current, ct).ConfigureAwait(false);
        DateTimeOffset now = _time.GetUtcNow();
        if (outcome.IsSuccess)
        {
            Remove(faultId);
            AddHistory(new FaultHealRecord(faultId, current.Kind, current.TargetId, reason, now, true));
            await PersistBestEffortAsync(ct).ConfigureAwait(false);
            Observe(ObservationKind.FaultHeal, current, "healed");
            return Result.Success();
        }

        Replace(current with { State = FaultState.HealFailed, HealError = outcome.Error });
        AddHistory(new FaultHealRecord(faultId, current.Kind, current.TargetId, reason, now, false));
        await PersistBestEffortAsync(ct).ConfigureAwait(false);
        Observe(ObservationKind.FaultHeal, current, "heal_failed");
        _logger.LogWarning("lab.faults heal of kind {Kind} failed; the entry stays journalled", current.Kind);
        return Result.Failure(outcome.Error);
    }

    private async Task<Result> HealWithRetryAsync(FaultJournalEntry entry, CancellationToken ct)
    {
        if (!_kinds.TryGetValue(entry.Kind, out IFaultKind? kind))
        {
            return Result.Failure("[Lab.InvalidArgument] unknown fault kind");
        }

        Result first = await RunKindAsync(() => kind.HealAsync(entry, ct)).ConfigureAwait(false);
        if (first.IsSuccess)
        {
            return first;
        }

        return await RunKindAsync(() => kind.HealAsync(entry, ct)).ConfigureAwait(false);
    }

    private async Task<Result<IReadOnlyList<FaultJournalEntry>>> ReadJournalAsync(CancellationToken ct)
    {
        try
        {
            return await _journal.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<IReadOnlyList<FaultJournalEntry>>.Failure($"[Lab.Journal] fault journal is not readable ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// Writes the whole in-memory set to the journal. Caller holds the gate.
    /// </summary>
    private async Task<Result> PersistAsync(CancellationToken ct)
    {
        IReadOnlyList<FaultJournalEntry> snapshot;
        lock (_state)
        {
            snapshot = _entries.Values.OrderBy(e => e.StartedAt).ToArray();
        }

        try
        {
            return await _journal.WriteAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("lab.faults journal write failed with {ExceptionType}", ex.GetType().Name);
            return Result.Failure("[Lab.Journal] fault journal could not be written");
        }
    }

    private async Task PersistBestEffortAsync(CancellationToken ct)
    {
        Result written = await PersistAsync(ct).ConfigureAwait(false);
        if (written.IsFailure)
        {
            // The in-memory state stays authoritative for this run; the next successful write or recovery corrects the file.
            _logger.LogWarning("lab.faults journal is behind the in-memory state");
        }
    }

    private async Task<Result<T>> RunKindAsync<T>(Func<Task<Result<T>>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only the exception type is logged: the message can carry target detail.
            _logger.LogWarning("lab.faults fault kind threw {ExceptionType}", ex.GetType().Name);
            return Result<T>.Failure("[Lab.FaultKind] fault kind failed");
        }
    }

    private async Task<Result> RunKindAsync(Func<Task<Result>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("lab.faults fault kind threw {ExceptionType}", ex.GetType().Name);
            return Result.Failure("[Lab.FaultKind] fault kind failed");
        }
    }

    private void Observe(ObservationKind kind, FaultJournalEntry entry, string status)
    {
        string? runId;
        lock (_state)
        {
            runId = _runId;
        }

        if (_observations is null || runId is null)
        {
            return;
        }

        var args = JsonSerializer.SerializeToElement(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fault_id"] = entry.FaultId,
            ["kind"] = entry.Kind,
            ["target_id"] = entry.TargetId,
        });
        var result = JsonSerializer.SerializeToElement(new Dictionary<string, string>(StringComparer.Ordinal) { ["status"] = status });
        var draft = new LabObservation(0, runId, _time.GetUtcNow(), "faults", null, kind, entry.Kind, args, null, result, entry.FaultId);
        Result<long> appended = _observations.Append(draft);
        if (appended.IsFailure)
        {
            _logger.LogWarning("lab.faults observation was not recorded");
        }
    }

    private void Replace(FaultJournalEntry entry)
    {
        lock (_state)
        {
            _entries[entry.FaultId] = entry;
        }
    }

    private void Remove(string faultId)
    {
        lock (_state)
        {
            _entries.Remove(faultId);
        }
    }

    private void AddHistory(FaultHealRecord record)
    {
        lock (_state)
        {
            _history.Add(record);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(0, _history.Count - MaxHistory);
            }
        }
    }

    private static ActiveFault ToActive(FaultJournalEntry entry) =>
        new(entry.FaultId, entry.Kind, entry.TargetId, entry.StartedAt, entry.Deadline, entry.State, entry.HealError);

    private static string NewFaultId() => "f-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
}
