using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Pool;
using Dhadgar.Lab.Random;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// The arguments of one run. <see cref="Seed"/> is null when the run draws its own; <see cref="LeaseId"/> is null when the run acquires its own lease.
/// </summary>
public sealed record UatRunRequest(UatSpec Spec, IReadOnlyCollection<string>? RowFilter, ulong? Seed, string? LeaseId, string? Label, bool PlanOnly);

/// <summary>
/// One clause after evaluation. The evidence fields (observed value, attempts, check time) go only to the bundle, so they
/// are not part of the <c>uat_run</c> result.
/// </summary>
public sealed record UatClauseResult(string Id, ClauseState State, string? Error, [property: JsonIgnore] JsonElement? Observed = null, [property: JsonIgnore] int Attempts = 0, [property: JsonIgnore] DateTimeOffset? CheckedAt = null);

/// <summary>
/// One step as received: the tool's own outcome, before a step that expected an error is rewritten. Bundle evidence only.
/// </summary>
public sealed record UatStepResult(string Id, string? Tool, bool Ok, string? Error, string? ErrorCode, string? ExpectedError, long DurationMs, JsonElement? Result);

/// <summary>
/// One recorded row: its grade, the reason, and its clauses (empty when the row stopped before its clauses ran). The steps
/// go only to the bundle.
/// </summary>
public sealed record UatRowResult(string RowId, RowGrade Grade, string Reason, IReadOnlyList<UatClauseResult> Clauses, [property: JsonIgnore] IReadOnlyList<UatStepResult>? Steps = null, [property: JsonIgnore] DateTimeOffset? StartedAt = null, [property: JsonIgnore] DateTimeOffset? EndedAt = null, [property: JsonIgnore] string? Member = null, [property: JsonIgnore] IReadOnlyList<UatRowViolation>? Violations = null, [property: JsonIgnore] IReadOnlyList<InvariantCheckError>? CheckErrors = null, [property: JsonIgnore] IReadOnlyList<UatRowFault>? Faults = null, [property: JsonIgnore] IReadOnlyList<InvariantSkip>? Skipped = null, [property: JsonIgnore] IReadOnlyList<string>? TwinPointers = null);

/// <summary>
/// One fault a row injected: the spec id and tool, the fault id the tool returned, when it went in and came out, and the heal error if any.
/// Bundle evidence only (C9 row <c>faults</c>, <c>repro.toml</c> <c>[[fault]]</c>).
/// </summary>
/// <param name="Kind">The fault kind the inject returned (L25f); null when the inject did not return one.</param>
/// <param name="TargetId">The target the inject returned (L25f, the <c>faults.target_id</c> column); null when absent.</param>
public sealed record UatRowFault(string Id, string Tool, JsonElement Args, string FaultId, DateTimeOffset InjectedAt, DateTimeOffset? HealedAt, string? HealError,
    string? Kind = null, string? TargetId = null);

/// <summary>
/// One invariant violation found in a row's window, or in a synthetic <c>invariants/&lt;id&gt;</c> row. <see cref="Allowed"/> is true when the row's
/// <c>allow_invariants</c> (section or row) lists the invariant; the violation is still recorded.
/// </summary>
public sealed record UatRowViolation(InvariantViolation Violation, bool Allowed);

/// <summary>
/// The result of one run. <see cref="Seed"/> is the seed the rows ran with (the section seed when the section sets one).
/// <see cref="LeaseOwner"/> is the owner of the lease the run held; it goes only to the bundle, never to the <c>uat_run</c> result.
/// <see cref="Pair"/> is the experiment pair of a twin run (C18); it goes only to the bundle's <c>run.json</c>.
/// </summary>
public sealed record UatRunResult(string RunId, ulong Seed, bool PlanOnly, RunOverall Overall, IReadOnlyList<UatRowResult> Rows, long Events = 0, bool Truncated = false, [property: JsonIgnore] string? LeaseOwner = null, [property: JsonIgnore] InvariantReport? Invariants = null, [property: JsonIgnore] PairInfo? Pair = null);

/// <summary>
/// Runs a loaded spec through <see cref="LabToolDispatcher"/> (C8, R-CONTRACT). Rows run in file order; a row whose dependency did not
/// pass is SKIPPED; a step error makes the row BLOCKED and stops it; the run continues with the next row. Each row is run by <see cref="RowExecutor"/>.
/// </summary>
public sealed class UatRunner
{
    /// <summary>The TTL of a run's own lease, seconds. Every step renews it.</summary>
    internal const int OwnLeaseTtlSeconds = 600;

    private const string OwnLeasePrefix = "uat-run/";
    private const string DefaultPurpose = "uat";

    private readonly LeaseBook _leases;
    private readonly RowExecutor _rows;
    private readonly ICapabilityProbe _probe;
    private readonly TimeProvider _time;
    private readonly ILabObservationSink? _observations;
    private readonly string? _uatRuns;
    private readonly InvariantMonitor? _invariants;
    private readonly InvariantLedger? _ledger;
    private readonly DaemonIdentity? _daemon;
    private readonly PoolManager? _pool;
    private readonly IPoolMemberPreparer? _preparer;

    /// <summary>
    /// Creates the runner. With <paramref name="observations"/> and <paramref name="paths"/> every run and step is recorded in the run's <c>events.jsonl</c> (C18).
    /// <paramref name="settle"/> is the wait after a row with faults before its clauses run; null uses <see cref="RowExecutor.DefaultSettle"/>.
    /// <paramref name="preparer"/> installs what a pool member needs (the CLI) after the pool acquires or resets it (D-ML173); null prepares nothing.
    /// </summary>
    public UatRunner(LabToolDispatcher dispatcher, LeaseBook leases, ClauseEvaluator evaluator, ICapabilityProbe probe, TimeProvider time, ILabObservationSink? observations = null, LabPaths? paths = null, InvariantMonitor? invariants = null, InvariantLedger? ledger = null, DaemonIdentity? daemon = null, TimeSpan? settle = null, PoolManager? pool = null, IPoolMemberPreparer? preparer = null)
    {
        _pool = pool;
        _preparer = preparer;
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(evaluator);
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _rows = new RowExecutor(dispatcher, leases, evaluator, time, observations, CheckpointAsync, settle ?? RowExecutor.DefaultSettle, pool is null ? null : (id, ct) => ResetAndPrepareAsync(pool, id, ct));
        _observations = observations;
        _uatRuns = paths?.UatRuns;
        _invariants = invariants;
        _ledger = ledger;
        _daemon = daemon;
    }

    /// <summary>True when a preparer is wired, so every member the run acquires or resets is prepared (for the wiring test).</summary>
    internal bool PreparesMembers => _preparer is not null;

    /// <summary>
    /// Resets one member, then prepares it. A preparation failure is returned as the row's failure (D-ML173).
    /// </summary>
    private async Task<Result<PoolMember>> ResetAndPrepareAsync(PoolManager pool, string memberId, CancellationToken ct)
    {
        Result<PoolMember> reset = await pool.ResetAsync(memberId, ct).ConfigureAwait(false);
        if (reset.IsFailure || _preparer is null)
        {
            return reset;
        }

        Result prepared = await _preparer.PrepareAsync(reset.Value!, ct).ConfigureAwait(false);
        return prepared.IsFailure ? Result<PoolMember>.Failure(prepared.Error) : reset;
    }

    /// <summary>
    /// Runs one spec. A held lease refuses the run before any row runs (<c>[Lab.LeaseHeld]</c>). Never throws for run failures.
    /// </summary>
    /// <param name="request">The run arguments.</param>
    /// <param name="sink">Receives the run's progress.</param>
    /// <param name="ct">Cancels the run.</param>
    public Task<Result<UatRunResult>> RunAsync(UatRunRequest request, IUatRunSink sink, CancellationToken ct)
    {
        return RunAsync(request, sink, null, ct);
    }

    /// <summary>
    /// Runs one spec with a twin overlay (L53, C19). The overlay is internal: no tool argument reaches it. Its pre rows run first and are graded and recorded;
    /// its extra faults are injected before the steps of every other row of the run. A null overlay is an ordinary run.
    /// </summary>
    /// <param name="request">The run arguments.</param>
    /// <param name="sink">Receives the run's progress.</param>
    /// <param name="overlay">The twin overlay, or null.</param>
    /// <param name="ct">Cancels the run.</param>
    internal async Task<Result<UatRunResult>> RunAsync(UatRunRequest request, IUatRunSink sink, UatRunOverlay? overlay, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);

        if (overlay is not null)
        {
            Result overlayCheck = CheckOverlay(request.Spec, overlay);
            if (overlayCheck.IsFailure)
            {
                return Result<UatRunResult>.Failure(overlayCheck.Error);
            }
        }

        ulong runSeed = request.Seed ?? LabRandom.NewSeed();
        string runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        ulong seed = request.Spec.Section.Seed ?? runSeed;

        if (request.PlanOnly)
        {
            var planned = new UatRunResult(runId, seed, true, RunOverall.Incomplete, Array.Empty<UatRowResult>(), Pair: overlay?.Pair);
            sink.RunFinished(planned);
            return Result<UatRunResult>.Success(planned);
        }

        string? leaseId = request.LeaseId;
        bool ownLease = leaseId is null;
        string leaseOwner;
        if (leaseId is null)
        {
            Result<LeaseGrant> grant = _leases.Acquire(OwnLeasePrefix + runId, string.IsNullOrWhiteSpace(request.Label) ? DefaultPurpose : request.Label, OwnLeaseTtlSeconds, false, null);
            if (grant.IsFailure)
            {
                return Result<UatRunResult>.Failure(grant.Error);
            }

            leaseId = grant.Value!.LeaseId;
            leaseOwner = OwnLeasePrefix + runId;
        }
        else
        {
            Result<LeaseInfo> touched = _leases.Touch(leaseId);
            if (touched.IsFailure)
            {
                return Result<UatRunResult>.Failure(touched.Error);
            }

            leaseOwner = touched.Value!.Owner;
        }

        bool observing = false;
        IReadOnlyList<string> acquiredMembers = [];
        try
        {
            var run = new RowExecutor.RunContext(runId, seed, leaseId, ownLease, _time.GetUtcNow());
            sink.RunStarted(runId, seed, request.Spec);
            if (_observations is not null && _uatRuns is not null)
            {
                // The sink names the folder once the run has started; the fallback uses the same name rule.
                string folder = Path.Combine(_uatRuns, sink.FolderName ?? BundleFolders.Name(run.RunStart, "nobuild", runId));
                observing = _observations.BeginRun(runId, folder).IsSuccess;
            }

            string? missing = await MissingCapabilityAsync(request.Spec.Section, ct).ConfigureAwait(false);

            // requires_pool (L30): one acquire per section; a failure SKIPs the section's rows with "pool: <error>".
            if (missing is null && request.Spec.Section.RequiresPool is { } poolSpec && _pool is not null && leaseId is not null)
            {
                Result<IReadOnlyList<PoolMember>> acquired = await _pool
                    .AcquireAsync(ConstraintsOf(poolSpec), "uat " + request.Spec.Section.Id, leaseId, ct)
                    .ConfigureAwait(false);
                if (acquired.IsSuccess)
                {
                    run.Members = acquired.Value.Select(m => m.MemberId).ToArray();
                    acquiredMembers = run.Members;

                    // Prepare each acquired member in order; the first failure becomes the section's pool error. The release loop still releases them.
                    if (_preparer is not null)
                    {
                        foreach (PoolMember member in acquired.Value)
                        {
                            Result prepared = await _preparer.PrepareAsync(member, ct).ConfigureAwait(false);
                            if (prepared.IsFailure)
                            {
                                run.PoolError = prepared.Error;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    run.PoolError = acquired.Error;
                }
            }

            // Checkpoint at run start: the window runs from the daemon's start, so earlier leaks and orphans are reported.
            var startWindow = new InvariantWindow(_daemon?.StartedAt ?? run.RunStart, _time.GetUtcNow(), null, null);
            InvariantReport startReport = await CheckpointAsync(startWindow, ct).ConfigureAwait(false);
            run.CoveredTo = startWindow.To;

            var rows = new List<UatRowResult>();
            var recorded = new Dictionary<string, RowGrade>(StringComparer.Ordinal);

            // The pre rows (twin overlay) run first, without the extra faults; the other rows carry them.
            IReadOnlyList<UatFault> extraFaults = overlay is null ? Array.Empty<UatFault>() : ExtraFaultsOf(overlay);
            var order = new List<(UatRow Row, bool Pre)>();
            if (overlay is not null)
            {
                order.AddRange(overlay.PreRows.Select(id => (request.Spec.Rows.First(r => r.Id == id), true)));
            }

            foreach (UatRow row in request.Spec.Rows)
            {
                if (overlay is not null && overlay.PreRows.Contains(row.Id, StringComparer.Ordinal))
                {
                    continue;
                }

                if (request.RowFilter is { } filter && !filter.Contains(row.Id))
                {
                    continue;
                }

                order.Add((row, false));
            }

            foreach ((UatRow row, bool pre) in order)
            {
                DateTimeOffset rowStarted = _time.GetUtcNow();
                UatRowResult result = await _rows.RunRowAsync(request.Spec, row, missing, recorded, run, pre ? Array.Empty<UatFault>() : extraFaults, ct).ConfigureAwait(false);
                result = result with { StartedAt = rowStarted, EndedAt = _time.GetUtcNow() };
                recorded[row.Id] = result.Grade;
                rows.Add(result);
                sink.RowFinished(result);
            }

            // Checkpoint at run end: from the last covered time (the end of the last row checkpoint) to now. Rows already cover their own windows.
            InvariantReport endReport = await CheckpointAsync(new InvariantWindow(run.CoveredTo, _time.GetUtcNow(), null, null), ct).ConfigureAwait(false);

            var invariantReport = new InvariantReport(
                startReport.Violations.Concat(rows.SelectMany(r => r.Violations ?? []).Select(v => v.Violation)).Concat(endReport.Violations).ToArray(),
                startReport.Errors.Concat(rows.SelectMany(r => r.CheckErrors ?? [])).Concat(endReport.Errors).ToArray(),
                (startReport.Skipped ?? []).Select(s => s with { Row = "(run-start)" })
                    .Concat(rows.SelectMany(r => r.Skipped ?? []))
                    .Concat((endReport.Skipped ?? []).Select(s => s with { Row = "(run-end)" }))
                    .ToArray());

            // Violations and check errors outside every row window (the run start and end checkpoints) become synthetic rows (C8, D-ML24).
            foreach (UatRowResult synthetic in SyntheticRows(startReport.Violations.Concat(endReport.Violations).ToArray(), startReport.Errors.Concat(endReport.Errors).ToArray()))
            {
                rows.Add(synthetic);
                sink.RowFinished(synthetic);
            }

            long events = 0;
            if (observing)
            {
                observing = false;
                Result<long> ended = await _observations!.EndRunAsync(runId, CancellationToken.None).ConfigureAwait(false);
                events = ended.IsSuccess ? ended.Value : 0;
            }

            // The sink stops counting at MaxObservationsPerRun and writes one extra "truncated" note, so a larger count means truncation.
            bool truncated = events > LabObservationSink.MaxObservationsPerRun;
            // Unmatched overall (D-ML92): used only when no bundle exists (plan_only); the bundle and the ingest carry their own.
            var graded = rows.Select(row => new GradedRow(request.Spec.Section.Id, row.RowId, row.Grade)).ToList();
            RunOverall overall = RunSummary.Compute(graded, new Dictionary<string, KnownMatch>(StringComparer.Ordinal)).Overall;
            var outcome = new UatRunResult(runId, seed, false, overall, rows, events, truncated, leaseOwner, invariantReport, overlay?.Pair);
            sink.RunFinished(outcome);
            return Result<UatRunResult>.Success(outcome);
        }
        finally
        {
            if (observing)
            {
                _ = await _observations!.EndRunAsync(runId, CancellationToken.None).ConfigureAwait(false);
            }

            // The section's members are released at section end, also when a row blocked or the run was cancelled. The host is refused and stays.
            foreach (string member in acquiredMembers.Where(m => m != "host"))
            {
                _ = await _pool!.ReleaseAsync(member, CancellationToken.None).ConfigureAwait(false);
            }

            if (ownLease && leaseId is not null)
            {
                _ = _leases.Release(leaseId);
            }
        }
    }

    /// <summary>
    /// Refuses an overlay whose pre rows are not rows of the spec, repeated, or from another spec file (L53). Returns success when the overlay fits the spec.
    /// </summary>
    private static Result CheckOverlay(UatSpec spec, UatRunOverlay overlay)
    {
        if (overlay.PreSpec is not null && !string.Equals(overlay.PreSpec, Path.GetFileName(spec.File), StringComparison.Ordinal))
        {
            return Result.Failure("[Lab.InvalidArgument] the twin pre spec is not the run's spec");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in overlay.PreRows)
        {
            if (!seen.Add(id))
            {
                return Result.Failure("[Lab.InvalidArgument] the twin pre row " + id + " is repeated");
            }

            if (!spec.Rows.Any(r => r.Id == id))
            {
                return Result.Failure("[Lab.NotFound] the twin pre row " + id + " is not in the spec");
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// The overlay's extra faults as spec faults: injected before the steps and healed at row end (heal <c>never</c>, so the row's final heal takes them).
    /// Each id names the twin and its role, so the seeded fault stream is per twin (R-SEED).
    /// </summary>
    private static UatFault[] ExtraFaultsOf(UatRunOverlay overlay)
    {
        string owner = overlay.Pair is { } pair ? "twin:" + pair.ExperimentId + ":" + pair.Role : "twin";
        return overlay.ExtraFaults
            .Select((fault, index) => new UatFault(owner + ":" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), fault.Tool, fault.Args, "before_steps", "never"))
            .ToArray();
    }

    /// <summary>
    /// Runs one invariant checkpoint over the window and records it in the ledger. A run without a monitor reports nothing.
    /// </summary>
    private async Task<InvariantReport> CheckpointAsync(InvariantWindow window, CancellationToken ct)
    {
        if (_invariants is null)
        {
            return new InvariantReport([], []);
        }

        InvariantReport report = await _invariants.CheckpointAsync(window, ct).ConfigureAwait(false);
        _ledger?.Record(window, report);
        return report;
    }

    /// <summary>
    /// Builds one synthetic row per invariant outside every row window: FAIL when it has a violation, otherwise UNVERIFIED for a check error.
    /// </summary>
    private static IEnumerable<UatRowResult> SyntheticRows(IReadOnlyList<InvariantViolation> violations, IReadOnlyList<InvariantCheckError> errors)
    {
        IEnumerable<string> ids = violations.Select(v => v.InvariantId).Concat(errors.Select(e => e.InvariantId)).Distinct(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            UatRowViolation[] own = violations.Where(v => v.InvariantId == id).Select(v => new UatRowViolation(v, false)).ToArray();
            InvariantCheckError[] ownErrors = errors.Where(e => e.InvariantId == id).ToArray();
            (RowGrade grade, string reason) = RowGrader.Grade(true, null, null, Array.Empty<(bool, ClauseState)>(), own.Select(v => (v.Violation.InvariantId, v.Allowed)).ToArray(), ownErrors);
            yield return new UatRowResult("invariants/" + id, grade, reason, Array.Empty<UatClauseResult>(), Violations: own, CheckErrors: ownErrors);
        }
    }

    /// <summary>
    /// Reads the section's <c>requires_pool</c> table into C13 constraints. Keys were checked by the spec validator; a value of the wrong type is left unset.
    /// </summary>
    private static PoolConstraints ConstraintsOf(IReadOnlyDictionary<string, JsonElement> table)
    {
        string? Text(string key) => table.TryGetValue(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int? Number(string key) => table.TryGetValue(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;
        bool? Flag(string key) => table.TryGetValue(key, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

        return new PoolConstraints(Text("os"), Number("min_os_build"), Text("sku"), Text("isolation"), Text("kind"), Text("arch"), Flag("reboot"), Number("count") ?? 1);
    }

    private async Task<string?> MissingCapabilityAsync(UatSection section, CancellationToken ct)
    {
        foreach (string capability in section.Requires)
        {
            if (!await _probe.IsAvailableAsync(capability, ct).ConfigureAwait(false))
            {
                return capability;
            }
        }

        return null;
    }
}
