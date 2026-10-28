using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Random;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// The arguments of one run. <see cref="Seed"/> is null when the run draws its own; <see cref="LeaseId"/> is null when the run acquires its own lease.
/// </summary>
public sealed record UatRunRequest(UatSpec Spec, IReadOnlyCollection<string>? RowFilter, ulong? Seed, string? LeaseId, string? Label, bool PlanOnly);

/// <summary>
/// One clause after evaluation.
/// </summary>
public sealed record UatClauseResult(string Id, ClauseState State, string? Error);

/// <summary>
/// One recorded row: its grade, the reason, and its clauses (empty when the row stopped before its clauses ran).
/// </summary>
public sealed record UatRowResult(string RowId, RowGrade Grade, string Reason, IReadOnlyList<UatClauseResult> Clauses);

/// <summary>
/// The result of one run. <see cref="Seed"/> is the seed the rows ran with (the section seed when the section sets one).
/// </summary>
public sealed record UatRunResult(string RunId, ulong Seed, bool PlanOnly, RunOverall Overall, IReadOnlyList<UatRowResult> Rows, long Events = 0, bool Truncated = false);

/// <summary>
/// Runs a loaded spec through <see cref="LabToolDispatcher"/> (C8, R-CONTRACT). Rows run in file order; a row whose dependency did not
/// pass is SKIPPED; a step error makes the row BLOCKED and stops it; the run continues with the next row.
/// </summary>
public sealed class UatRunner
{
    /// <summary>The TTL of a run's own lease, seconds. Every step renews it.</summary>
    internal const int OwnLeaseTtlSeconds = 600;

    private const string LeaseLostCode = "Lab.LeaseLost";
    private const string DefaultErrorCode = "Lab.Unavailable";
    private const string FaultsDeferred = "[Lab.Unavailable] faults arrive in L22";
    private const string PoolDeferred = "pool unavailable";
    private const string OwnLeasePrefix = "uat-run/";
    private const string DefaultPurpose = "uat";
    private const string LeaseIdField = "lease_id";
    private const string RunnerActor = "runner";

    private static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new JsonObject());

    private readonly LabToolDispatcher _dispatcher;
    private readonly LeaseBook _leases;
    private readonly ClauseEvaluator _evaluator;
    private readonly ICapabilityProbe _probe;
    private readonly TimeProvider _time;
    private readonly ILabObservationSink? _observations;
    private readonly string? _uatRuns;

    /// <summary>
    /// Creates the runner. With <paramref name="observations"/> and <paramref name="paths"/> every run and step is recorded in the run's <c>events.jsonl</c> (C18).
    /// </summary>
    public UatRunner(LabToolDispatcher dispatcher, LeaseBook leases, ClauseEvaluator evaluator, ICapabilityProbe probe, TimeProvider time, ILabObservationSink? observations = null, LabPaths? paths = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _observations = observations;
        _uatRuns = paths?.UatRuns;
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
        if (leaseId is null)
        {
            Result<LeaseGrant> grant = _leases.Acquire(OwnLeasePrefix + runId, string.IsNullOrWhiteSpace(request.Label) ? DefaultPurpose : request.Label, OwnLeaseTtlSeconds, false, null);
            if (grant.IsFailure)
            {
                return Result<UatRunResult>.Failure(grant.Error);
            }

            leaseId = grant.Value!.LeaseId;
        }
        else
        {
            Result<LeaseInfo> touched = _leases.Touch(leaseId);
            if (touched.IsFailure)
            {
                return Result<UatRunResult>.Failure(touched.Error);
            }
        }

        bool observing = false;
        try
        {
            var run = new RunContext(runId, seed, leaseId, ownLease, _time.GetUtcNow());
            sink.RunStarted(runId, seed, request.Spec);
            if (_observations is not null && _uatRuns is not null)
            {
                // The bundle folder name (BundleWriter) is yyyyMMdd-<build id>-<run id>; UAT runs are not built at start, so the build id is "nobuild".
                string folder = Path.Combine(_uatRuns, $"{_time.GetUtcNow().UtcDateTime:yyyyMMdd}-nobuild-{runId}");
                observing = _observations.BeginRun(runId, folder).IsSuccess;
            }

            string? missing = await MissingCapabilityAsync(request.Spec.Section, ct).ConfigureAwait(false);

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

                UatRowResult result = await RunRowAsync(request.Spec, row, missing, recorded, run, ct).ConfigureAwait(false);
                grades.Add(result.Grade);
                recorded[row.Id] = result.Grade;
                if (result.Grade is RowGrade.Fail or RowGrade.Blocked)
                {
                    failures.Add((row.Id, false));
                }

                rows.Add(result);
                sink.RowFinished(result);
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
            var outcome = new UatRunResult(runId, seed, false, RunOutcome.Compute(grades, failures), rows, events, truncated);
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

    private async Task<UatRowResult> RunRowAsync(UatSpec spec, UatRow row, string? missing, Dictionary<string, RowGrade> recorded, RunContext run, CancellationToken ct)
    {
        if (run.LeaseLost)
        {
            return Blocked(row, "[Lab.LeaseLost] the lease ended before this row");
        }

        if (spec.Section.RequiresPool is not null)
        {
            return Skipped(row, PoolDeferred);
        }

        if (missing is not null)
        {
            return Skipped(row, "missing capability: " + missing);
        }

        foreach (string dependency in row.DependsOn)
        {
            if (!recorded.TryGetValue(dependency, out RowGrade grade) || grade != RowGrade.Pass)
            {
                return Skipped(row, "depends on " + dependency);
            }
        }

        if (row.Faults.Count > 0)
        {
            return Blocked(row, FaultsDeferred);
        }

        return await ExecuteRowAsync(spec.Section.Id, row, run, ct).ConfigureAwait(false);
    }

    private async Task<UatRowResult> ExecuteRowAsync(string sectionId, UatRow row, RunContext run, CancellationToken ct)
    {
        var vars = new UatVars(run.RunId, row.Id, sectionId, run.Seed, null);
        var stepStarts = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var steps = new Dictionary<string, StepOutcome>(StringComparer.Ordinal);
        var window = new ClauseWindow(run.RunStart, _time.GetUtcNow(), stepStarts, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal), steps);

        var baselines = new List<Result<JsonElement>>();
        foreach (UatClause clause in row.Expect)
        {
            baselines.Add(await _evaluator.CaptureBaselineAsync(clause, vars, window, ct).ConfigureAwait(false));
        }

        string? stepError = null;
        bool expectFailed = false;
        string? expectFailure = null;
        for (int i = 0; i < row.Steps.Count; i++)
        {
            UatStep step = row.Steps[i];
            string stepId = step.Id ?? "step" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            stepStarts[stepId] = _time.GetUtcNow();

            if (step.Tool is null)
            {
                int wait = step.WaitMs ?? 0;
                if (wait > 0)
                {
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                }

                steps[stepId] = new StepOutcome(true, null, null, wait, null);
                continue;
            }

            StepOutcome outcome = await ExecuteStepAsync(step, stepId, vars, run, ct).ConfigureAwait(false);
            if (step.ExpectError is { } expected)
            {
                if (MatchesExpected(expected, outcome))
                {
                    steps[stepId] = outcome with { Ok = true, Error = null };
                    continue;
                }

                steps[stepId] = outcome;
                if (outcome.Ok)
                {
                    expectFailed = true;
                    expectFailure = "expected error " + expected + ", got success";
                    continue;
                }

                stepError = outcome.Error;
                break;
            }

            steps[stepId] = outcome;
            if (!outcome.Ok)
            {
                stepError = outcome.Error;
                break;
            }

            if (outcome.Result is { } payload && step.Capture.Count > 0)
            {
                string? captureError = CaptureAll(step, payload, vars);
                if (captureError is not null)
                {
                    stepError = captureError;
                    break;
                }
            }
        }

        var clauseResults = new List<UatClauseResult>();
        var inputs = new List<(bool Required, ClauseState State)>();
        if (stepError is null)
        {
            for (int i = 0; i < row.Expect.Count; i++)
            {
                UatClause clause = row.Expect[i];
                ClauseEvaluation evaluation = await _evaluator.EvaluateAsync(clause, vars, window, baselines[i], ct).ConfigureAwait(false);
                clauseResults.Add(new UatClauseResult(clause.Id, evaluation.State, evaluation.Error));
                inputs.Add((clause.Required, evaluation.State));
            }
        }

        if (expectFailed)
        {
            inputs.Add((true, ClauseState.NotHeld));
        }

        (RowGrade grade, string reason) = RowGrader.Grade(true, null, stepError, inputs);
        if (expectFailed && grade == RowGrade.Fail)
        {
            reason = expectFailure!;
        }

        return new UatRowResult(row.Id, grade, reason, clauseResults);
    }

    private async Task<StepOutcome> ExecuteStepAsync(UatStep step, string stepId, UatVars vars, RunContext run, CancellationToken ct)
    {
        DateTimeOffset started = _time.GetUtcNow();
        if (run.OwnLease && run.LeaseId is { } ownLeaseId)
        {
            Result<LeaseInfo> renewed = _leases.Renew(ownLeaseId, OwnLeaseTtlSeconds);
            if (renewed.IsFailure)
            {
                run.LeaseLost = true;
                return Failed(stepId, LeaseLostCode);
            }
        }

        Result<JsonElement> expanded = vars.Expand(step.Args ?? EmptyObject);
        if (expanded.IsFailure)
        {
            return Failed(stepId, DefaultErrorCode);
        }

        string tool = step.Tool!;
        JsonElement args = expanded.Value;
        if (ToolPolicy.GateFor(tool) is LabGate.Leased or LabGate.OwnLease)
        {
            args = WithLease(args, run.LeaseId);
        }

        var context = new LabCallContext(Guid.NewGuid().ToString("N"), run.LeaseId, "uat:" + run.RunId);
        OpId? op = BeginStepObservation(run.RunId, tool, args);
        LabToolResult call = await _dispatcher.InvokeAsync(tool, args, context, ct).ConfigureAwait(false);
        CompleteStepObservation(op, run.RunId, tool, call);
        long elapsed = (long)(_time.GetUtcNow() - started).TotalMilliseconds;
        if (!call.IsError)
        {
            return new StepOutcome(true, null, null, elapsed, call.Payload);
        }

        string code = ErrorCodeOf(call.Payload);
        if (code == LeaseLostCode)
        {
            run.LeaseLost = true;
        }

        return new StepOutcome(false, "[" + code + "] step " + stepId + " failed", code, elapsed, call.Payload);
    }

    /// <summary>
    /// Records the invocation of a step (C18). Returns null when the runner has no sink.
    /// </summary>
    private OpId? BeginStepObservation(string runId, string tool, JsonElement args)
    {
        if (_observations is null)
        {
            return null;
        }

        OpId op = _observations.NextOp(runId, RunnerActor);
        _ = _observations.Append(new LabObservation(0, runId, _time.GetUtcNow(), RunnerActor, op.ToString(), ObservationKind.Invoke, tool, args, null, EmptyObject, null));
        return op;
    }

    /// <summary>
    /// Records the completion of a step (C18). <c>http_request</c> and <c>nodes_enroll_probe</c> report their own outcome.
    /// </summary>
    private void CompleteStepObservation(OpId? op, string runId, string tool, LabToolResult call)
    {
        if (_observations is null || op is not { } id)
        {
            return;
        }

        _ = _observations.Append(new LabObservation(0, runId, _time.GetUtcNow(), RunnerActor, id.ToString(), ObservationKind.Complete, tool, EmptyObject, OutcomeOf(tool, call), call.Payload, null));
    }

    private static Dhadgar.Lab.Observe.OpOutcome OutcomeOf(string tool, LabToolResult call)
    {
        if (tool is "http_request" or "nodes_enroll_probe"
            && call.Payload.ValueKind == JsonValueKind.Object
            && call.Payload.TryGetProperty("outcome", out JsonElement outcome)
            && outcome.ValueKind == JsonValueKind.String)
        {
            return outcome.GetString() switch
            {
                "ok" => Dhadgar.Lab.Observe.OpOutcome.Ok,
                "unknown" => Dhadgar.Lab.Observe.OpOutcome.Unknown,
                _ => Dhadgar.Lab.Observe.OpOutcome.Fail,
            };
        }

        return call.IsError ? Dhadgar.Lab.Observe.OpOutcome.Fail : Dhadgar.Lab.Observe.OpOutcome.Ok;
    }

    private static string? CaptureAll(UatStep step, JsonElement payload, UatVars vars)
    {
        foreach ((string name, string path) in step.Capture)
        {
            Result captured = vars.Capture(name, payload, path);
            if (captured.IsFailure)
            {
                return captured.Error;
            }
        }

        return null;
    }

    private static JsonElement WithLease(JsonElement args, string? leaseId)
    {
        if (leaseId is null || JsonNode.Parse(args.GetRawText()) is not JsonObject obj)
        {
            return args;
        }

        obj[LeaseIdField] = leaseId;
        return JsonSerializer.SerializeToElement(obj);
    }

    private static bool MatchesExpected(string expected, StepOutcome outcome) =>
        outcome.ErrorCode == expected
        || (outcome.Result is { } payload && StatusOf(payload) is { } status && "http:" + status.ToString(System.Globalization.CultureInfo.InvariantCulture) == expected);

    private static int? StatusOf(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("status", out JsonElement status)
        && status.ValueKind == JsonValueKind.Number
        && status.TryGetInt32(out int value)
            ? value
            : null;

    private static string ErrorCodeOf(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("error", out JsonElement error)
        && error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty("code", out JsonElement code)
        && code.ValueKind == JsonValueKind.String
        && code.GetString() is { Length: > 0 } text
            ? text
            : DefaultErrorCode;

    private static StepOutcome Failed(string stepId, string code) =>
        new(false, "[" + code + "] step " + stepId + " failed", code, 0, null);

    private static UatRowResult Skipped(UatRow row, string reason)
    {
        (RowGrade grade, string text) = RowGrader.Grade(false, reason, null, Array.Empty<(bool, ClauseState)>());
        return new UatRowResult(row.Id, grade, text, Array.Empty<UatClauseResult>());
    }

    private static UatRowResult Blocked(UatRow row, string error)
    {
        (RowGrade grade, string text) = RowGrader.Grade(true, null, error, Array.Empty<(bool, ClauseState)>());
        return new UatRowResult(row.Id, grade, text, Array.Empty<UatClauseResult>());
    }

    /// <summary>
    /// The state of one run that the row loop reads and writes.
    /// </summary>
    private sealed class RunContext
    {
        public RunContext(string runId, ulong seed, string? leaseId, bool ownLease, DateTimeOffset runStart)
        {
            RunId = runId;
            Seed = seed;
            LeaseId = leaseId;
            OwnLease = ownLease;
            RunStart = runStart;
        }

        public string RunId { get; }

        public ulong Seed { get; }

        public string? LeaseId { get; }

        public bool OwnLease { get; }

        public DateTimeOffset RunStart { get; }

        public bool LeaseLost { get; set; }
    }
}
