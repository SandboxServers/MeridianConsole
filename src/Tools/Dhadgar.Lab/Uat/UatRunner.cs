using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhadgar.Lab.Daemon;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Random;
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
public sealed record UatRowResult(string RowId, RowGrade Grade, string Reason, IReadOnlyList<UatClauseResult> Clauses, [property: JsonIgnore] IReadOnlyList<UatStepResult>? Steps = null, [property: JsonIgnore] DateTimeOffset? StartedAt = null, [property: JsonIgnore] DateTimeOffset? EndedAt = null, [property: JsonIgnore] string? Member = null, [property: JsonIgnore] IReadOnlyList<UatRowViolation>? Violations = null, [property: JsonIgnore] IReadOnlyList<InvariantCheckError>? CheckErrors = null);

/// <summary>
/// One invariant violation found in a row's window, or in a synthetic <c>invariants/&lt;id&gt;</c> row. <see cref="Allowed"/> is true when the row's
/// <c>allow_invariants</c> (section or row) lists the invariant; the violation is still recorded.
/// </summary>
public sealed record UatRowViolation(InvariantViolation Violation, bool Allowed);

/// <summary>
/// The result of one run. <see cref="Seed"/> is the seed the rows ran with (the section seed when the section sets one).
/// <see cref="LeaseOwner"/> is the owner of the lease the run held; it goes only to the bundle, never to the <c>uat_run</c> result.
/// </summary>
public sealed record UatRunResult(string RunId, ulong Seed, bool PlanOnly, RunOverall Overall, IReadOnlyList<UatRowResult> Rows, long Events = 0, bool Truncated = false, [property: JsonIgnore] string? LeaseOwner = null, [property: JsonIgnore] InvariantReport? Invariants = null);

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

    /// <summary>
    /// Creates the runner. With <paramref name="observations"/> and <paramref name="paths"/> every run and step is recorded in the run's <c>events.jsonl</c> (C18).
    /// <paramref name="settle"/> is the wait after a row with faults before its clauses run; null uses <see cref="RowExecutor.DefaultSettle"/>.
    /// </summary>
    public UatRunner(LabToolDispatcher dispatcher, LeaseBook leases, ClauseEvaluator evaluator, ICapabilityProbe probe, TimeProvider time, ILabObservationSink? observations = null, LabPaths? paths = null, InvariantMonitor? invariants = null, InvariantLedger? ledger = null, DaemonIdentity? daemon = null, TimeSpan? settle = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(evaluator);
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _rows = new RowExecutor(dispatcher, leases, evaluator, time, observations, CheckpointAsync, settle ?? RowExecutor.DefaultSettle);
        _observations = observations;
        _uatRuns = paths?.UatRuns;
        _invariants = invariants;
        _ledger = ledger;
        _daemon = daemon;
    }

    /// <summary>
    /// Runs one spec. A held lease refuses the run before any row runs (<c>[Lab.LeaseHeld]</c>). Never throws for run failures.
    /// </summary>
    /// <param name="request">The run arguments.</param>
    /// <param name="sink">Receives the run's progress.</param>
    /// <param name="ct">Cancels the run.</param>
    public async Task<Result<UatRunResult>> RunAsync(UatRunRequest request, IUatRunSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);

        ulong runSeed = request.Seed ?? LabRandom.NewSeed();
        string runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        ulong seed = request.Spec.Section.Seed ?? runSeed;

        if (request.PlanOnly)
        {
            var planned = new UatRunResult(runId, seed, true, RunOverall.Incomplete, Array.Empty<UatRowResult>());
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

            // Checkpoint at run start: the window runs from the daemon's start, so earlier leaks and orphans are reported.
            var startWindow = new InvariantWindow(_daemon?.StartedAt ?? run.RunStart, _time.GetUtcNow(), null, null);
            InvariantReport startReport = await CheckpointAsync(startWindow, ct).ConfigureAwait(false);
            run.CoveredTo = startWindow.To;

            var grades = new List<RowGrade>();
            var failures = new List<(string FailureKey, bool IsKnown)>();
            var rows = new List<UatRowResult>();
            var recorded = new Dictionary<string, RowGrade>(StringComparer.Ordinal);

            foreach (UatRow row in request.Spec.Rows)
            {
                if (request.RowFilter is { } filter && !filter.Contains(row.Id))
                {
                    continue;
                }

                DateTimeOffset rowStarted = _time.GetUtcNow();
                UatRowResult result = await _rows.RunRowAsync(request.Spec, row, missing, recorded, run, ct).ConfigureAwait(false);
                result = result with { StartedAt = rowStarted, EndedAt = _time.GetUtcNow() };
                grades.Add(result.Grade);
                recorded[row.Id] = result.Grade;
                if (result.Grade is RowGrade.Fail or RowGrade.Blocked)
                {
                    failures.Add((row.Id, false));
                }

                rows.Add(result);
                sink.RowFinished(result);
            }

            // Checkpoint at run end: from the last covered time (the end of the last row checkpoint) to now. Rows already cover their own windows.
            InvariantReport endReport = await CheckpointAsync(new InvariantWindow(run.CoveredTo, _time.GetUtcNow(), null, null), ct).ConfigureAwait(false);

            var invariantReport = new InvariantReport(
                startReport.Violations.Concat(rows.SelectMany(r => r.Violations ?? []).Select(v => v.Violation)).Concat(endReport.Violations).ToArray(),
                startReport.Errors.Concat(rows.SelectMany(r => r.CheckErrors ?? [])).Concat(endReport.Errors).ToArray());

            // Violations and check errors outside every row window (the run start and end checkpoints) become synthetic rows (C8, D-ML24).
            foreach (UatRowResult synthetic in SyntheticRows(startReport.Violations.Concat(endReport.Violations).ToArray(), startReport.Errors.Concat(endReport.Errors).ToArray()))
            {
                grades.Add(synthetic.Grade);
                if (synthetic.Grade is RowGrade.Fail)
                {
                    failures.Add((synthetic.RowId, false));
                }

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
            var outcome = new UatRunResult(runId, seed, false, RunOutcome.Compute(grades, failures), rows, events, truncated, leaseOwner, invariantReport);
            sink.RunFinished(outcome);
            return Result<UatRunResult>.Success(outcome);
        }
        finally
        {
            if (observing)
            {
                _ = await _observations!.EndRunAsync(runId, CancellationToken.None).ConfigureAwait(false);
            }

            if (ownLease && leaseId is not null)
            {
                _ = _leases.Release(leaseId);
            }
        }
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
