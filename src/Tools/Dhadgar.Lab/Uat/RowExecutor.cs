using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// Executes one row of a spec: the row gates (pool, capability, dependencies), the steps through <see cref="LabToolDispatcher"/>,
/// the clauses and the row grade (C8, R-CONTRACT). Extracted from <see cref="UatRunner"/> with no change in behaviour.
/// </summary>
internal sealed class RowExecutor
{
    private const string LeaseLostCode = "Lab.LeaseLost";
    private const string DefaultErrorCode = "Lab.Unavailable";
    private const string FaultsDeferred = "[Lab.Unavailable] faults arrive in L22";
    private const string PoolDeferred = "pool unavailable";
    private const string RunnerActor = "runner";

    private static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new JsonObject());

    private readonly LabToolDispatcher _dispatcher;
    private readonly LeaseBook _leases;
    private readonly ClauseEvaluator _evaluator;
    private readonly TimeProvider _time;
    private readonly ILabObservationSink? _observations;

    /// <summary>
    /// Creates the executor. With <paramref name="observations"/> every step is recorded in the run's <c>events.jsonl</c> (C18).
    /// </summary>
    public RowExecutor(LabToolDispatcher dispatcher, LeaseBook leases, ClauseEvaluator evaluator, TimeProvider time, ILabObservationSink? observations)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _observations = observations;
    }

    /// <summary>
    /// Runs one row after its gates. A gate that fails returns the row SKIPPED or BLOCKED without running its steps.
    /// </summary>
    internal async Task<UatRowResult> RunRowAsync(UatSpec spec, UatRow row, string? missing, Dictionary<string, RowGrade> recorded, RunContext run, CancellationToken ct)
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
        var stepResults = new List<UatStepResult>();
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
                    await Task.Delay(TimeSpan.FromMilliseconds(wait), _time, ct).ConfigureAwait(false);
                }

                steps[stepId] = new StepOutcome(true, null, null, wait, null);
                stepResults.Add(new UatStepResult(stepId, null, true, null, null, null, wait, null));
                continue;
            }

            StepOutcome outcome = await ExecuteStepAsync(step, stepId, vars, run, ct).ConfigureAwait(false);

            // Recorded as the tool returned it, before an expected error rewrites Ok and Error below.
            stepResults.Add(new UatStepResult(stepId, step.Tool, outcome.Ok, outcome.Error, outcome.ErrorCode, step.ExpectError, outcome.DurationMs, outcome.Result));
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
                clauseResults.Add(new UatClauseResult(clause.Id, evaluation.State, evaluation.Error, evaluation.Observed, evaluation.Attempts, evaluation.CheckedAt));
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

        return new UatRowResult(row.Id, grade, reason, clauseResults, stepResults);
    }

    private async Task<StepOutcome> ExecuteStepAsync(UatStep step, string stepId, UatVars vars, RunContext run, CancellationToken ct)
    {
        DateTimeOffset started = _time.GetUtcNow();
        if (run.OwnLease && run.LeaseId is { } ownLeaseId)
        {
            Result<LeaseInfo> renewed = _leases.Renew(ownLeaseId, UatRunner.OwnLeaseTtlSeconds);
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
        // The observation records the spec's arguments, never the injected lease_id (D-ML66, D-ML4).
        OpId? op = BeginStepObservation(run.RunId, tool, expanded.Value);
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

        obj[LabToolDispatcher.LeaseIdArgument] = leaseId;
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
    internal sealed class RunContext
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
