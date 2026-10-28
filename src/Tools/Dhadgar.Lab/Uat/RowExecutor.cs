using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Invariants;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Policy;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat.Eval;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// Executes one row of a spec: the row gates (pool, capability, dependencies), the faults (C7), the steps, the clauses and the
/// row grade (C8, R-CONTRACT). Faults are injected and healed by dispatching the C4 fault tools through <see cref="LabToolDispatcher"/>.
/// </summary>
internal sealed class RowExecutor
{
    /// <summary>The settle time after a row with faults, before its clauses run (Lab:Uat:SettleMs default).</summary>
    internal static readonly TimeSpan DefaultSettle = TimeSpan.FromMilliseconds(5000);

    private const string LeaseLostCode = "Lab.LeaseLost";
    private const string DefaultErrorCode = "Lab.Unavailable";
    private const string LabNotCleanReason = "[Lab.Unavailable] lab not clean";
    private const string PoolDeferred = "pool unavailable";
    private const string RunnerActor = "runner";
    private const string BeforeSteps = "before_steps";
    private const string AfterSteps = "after_steps";
    private const string AfterStepPrefix = "after_step:";
    private const string FaultIdField = "fault_id";

    private static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new JsonObject());

    private readonly LabToolDispatcher _dispatcher;
    private readonly LeaseBook _leases;
    private readonly ClauseEvaluator _evaluator;
    private readonly TimeProvider _time;
    private readonly ILabObservationSink? _observations;
    private readonly Func<InvariantWindow, CancellationToken, Task<InvariantReport>> _checkpoint;
    private readonly TimeSpan _settle;

    /// <summary>
    /// Creates the executor. With <paramref name="observations"/> every step is recorded in the run's <c>events.jsonl</c> (C18).
    /// <paramref name="checkpoint"/> runs one invariant checkpoint over a row window. <paramref name="settle"/> is the wait after a row with faults; zero skips it.
    /// </summary>
    public RowExecutor(LabToolDispatcher dispatcher, LeaseBook leases, ClauseEvaluator evaluator, TimeProvider time, ILabObservationSink? observations, Func<InvariantWindow, CancellationToken, Task<InvariantReport>> checkpoint, TimeSpan settle)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _observations = observations;
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _settle = settle;
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

        if (run.LabNotClean)
        {
            return Blocked(row, LabNotCleanReason);
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

        return await ExecuteRowAsync(spec, row, run, ct).ConfigureAwait(false);
    }

    private async Task<UatRowResult> ExecuteRowAsync(UatSpec spec, UatRow row, RunContext run, CancellationToken ct)
    {
        var vars = new UatVars(run.RunId, row.Id, spec.Section.Id, run.Seed, null);
        var stepStarts = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var steps = new Dictionary<string, StepOutcome>(StringComparer.Ordinal);
        var heals = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var faults = new FaultState(heals);
        var window = new ClauseWindow(run.RunStart, _time.GetUtcNow(), stepStarts, heals, steps);

        var baselines = new List<Result<JsonElement>>();
        foreach (UatClause clause in row.Expect)
        {
            baselines.Add(await _evaluator.CaptureBaselineAsync(clause, vars, window, ct).ConfigureAwait(false));
        }

        // Faults that start before the first step. An inject error stops the row; the faults already in place are healed below.
        string? faultError = null;
        foreach (UatFault fault in row.Faults.Where(f => f.Start == BeforeSteps))
        {
            faultError = await InjectAsync(fault, vars, run, faults, ct).ConfigureAwait(false);
            if (faultError is not null)
            {
                break;
            }
        }

        string? stepError = null;
        bool expectFailed = false;
        string? expectFailure = null;
        string? previousStep = null;
        var stepResults = new List<UatStepResult>();
        for (int i = 0; i < row.Steps.Count && faultError is null; i++)
        {
            UatStep step = row.Steps[i];
            if (previousStep is not null)
            {
                faultError = await AfterStepAsync(previousStep, row, vars, run, faults, ct).ConfigureAwait(false);
                if (faultError is not null)
                {
                    break;
                }
            }

            string stepId = step.Id ?? "step" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            previousStep = stepId;
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

        // The last step completed without stopping the row: its after_step faults still start and heal.
        if (faultError is null && stepError is null && previousStep is not null)
        {
            faultError = await AfterStepAsync(previousStep, row, vars, run, faults, ct).ConfigureAwait(false);
        }

        // Heal after_steps faults, then anything still in place at row end (C7 heal = never, or a heal step that did not run).
        string? healError = await HealDueAsync(heal => heal == AfterSteps, vars, run, faults, ct).ConfigureAwait(false);
        healError ??= await HealDueAsync(_ => true, vars, run, faults, ct).ConfigureAwait(false);
        faultError ??= healError;
        if (faultError is not null)
        {
            InvariantReport blockedReport = await CheckRowAsync(spec, row, run, ct).ConfigureAwait(false);
            return Blocked(row, faultError, AttributeViolations(blockedReport, spec, row), blockedReport.Errors);
        }

        // A row with faults waits for the system to settle before its clauses read the state (Lab:Uat:SettleMs).
        if (row.Faults.Count > 0 && stepError is null && _settle > TimeSpan.Zero)
        {
            await Task.Delay(_settle, _time, ct).ConfigureAwait(false);
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

        // The invariant checkpoint for [previous checkpoint, now] runs on every path that reached the steps, so a blocked row keeps its violations.
        InvariantReport invariants = await CheckRowAsync(spec, row, run, ct).ConfigureAwait(false);
        UatRowViolation[] violations = AttributeViolations(invariants, spec, row);
        (RowGrade grade, string reason) = RowGrader.Grade(true, null, stepError, inputs, violations.Select(v => (v.Violation.InvariantId, v.Allowed)).ToArray(), invariants.Errors);
        if (expectFailed && grade == RowGrade.Fail)
        {
            reason = expectFailure!;
        }

        return new UatRowResult(row.Id, grade, reason, clauseResults, stepResults, Violations: violations, CheckErrors: invariants.Errors);
    }

    /// <summary>
    /// Runs the invariant checkpoint over the time since the last checkpoint, up to now, for this row. The next checkpoint starts where this one ends.
    /// </summary>
    private async Task<InvariantReport> CheckRowAsync(UatSpec spec, UatRow row, RunContext run, CancellationToken ct)
    {
        var window = new InvariantWindow(run.CoveredTo, _time.GetUtcNow(), spec.Section.Id, row.Id);
        InvariantReport report = await _checkpoint(window, ct).ConfigureAwait(false);
        run.CoveredTo = window.To;
        return report;
    }

    /// <summary>
    /// Marks each violation allowed when the section's or the row's <c>allow_invariants</c> lists its invariant (D-ML24).
    /// </summary>
    private static UatRowViolation[] AttributeViolations(InvariantReport report, UatSpec spec, UatRow row)
    {
        var allowed = new HashSet<string>(spec.Section.AllowInvariants.Concat(row.AllowInvariants), StringComparer.Ordinal);
        return report.Violations.Select(v => new UatRowViolation(v, allowed.Contains(v.InvariantId))).ToArray();
    }

    /// <summary>
    /// Injects the faults that start after <paramref name="stepId"/>, then heals the ones that heal there.
    /// </summary>
    private async Task<string?> AfterStepAsync(string stepId, UatRow row, UatVars vars, RunContext run, FaultState faults, CancellationToken ct)
    {
        string start = AfterStepPrefix + stepId;
        foreach (UatFault fault in row.Faults.Where(f => f.Start == start))
        {
            string? error = await InjectAsync(fault, vars, run, faults, ct).ConfigureAwait(false);
            if (error is not null)
            {
                return error;
            }
        }

        return await HealDueAsync(heal => heal == start, vars, run, faults, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Injects one fault through the fault tool named in its spec. Returns the error, or null when the fault is in place.
    /// </summary>
    private async Task<string?> InjectAsync(UatFault fault, UatVars vars, RunContext run, FaultState faults, CancellationToken ct)
    {
        var step = Synthetic(fault.Tool, fault.Args);
        StepOutcome outcome = await ExecuteStepAsync(step, "inject:" + fault.Id, vars, run, ct).ConfigureAwait(false);
        if (!outcome.Ok)
        {
            return "fault " + fault.Id + ": " + outcome.Error;
        }

        if (outcome.Result is not { } payload || !TryFaultId(payload, out string faultId))
        {
            return "fault " + fault.Id + ": [" + DefaultErrorCode + "] the inject returned no fault id";
        }

        faults.Active.Add(new InjectedFault(fault, faultId));
        return null;
    }

    /// <summary>
    /// Heals every fault in place whose spec heal matches, in injection order. The first heal error resets the lab and returns.
    /// </summary>
    private async Task<string?> HealDueAsync(Func<string, bool> isDue, UatVars vars, RunContext run, FaultState faults, CancellationToken ct)
    {
        foreach (InjectedFault injected in faults.Active.Where(f => isDue(f.Fault.Heal)).ToList())
        {
            var args = new JsonObject { [FaultIdField] = injected.FaultId };
            StepOutcome outcome = await ExecuteStepAsync(Synthetic("fault_heal", JsonSerializer.SerializeToElement(args)), "heal:" + injected.Fault.Id, vars, run, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                await ResetAsync(faults, vars, run, ct).ConfigureAwait(false);
                return "heal " + injected.Fault.Id + ": " + outcome.Error;
            }

            faults.Active.Remove(injected);
            faults.Heals[injected.Fault.Id] = _time.GetUtcNow();
        }

        return null;
    }

    /// <summary>
    /// After a failed heal: <c>fault_reset</c>, then <c>faults_active</c>. Faults still in place make the lab not clean (R-REG), and
    /// every remaining row is BLOCKED.
    /// </summary>
    private async Task ResetAsync(FaultState faults, UatVars vars, RunContext run, CancellationToken ct)
    {
        _ = await ExecuteStepAsync(Synthetic("fault_reset", null), "reset", vars, run, ct).ConfigureAwait(false);
        StepOutcome active = await ExecuteStepAsync(Synthetic("faults_active", null), "faults_active", vars, run, ct).ConfigureAwait(false);
        if (active.Ok && active.Result is { } payload && NoFaultsActive(payload))
        {
            faults.Active.Clear();
            return;
        }

        run.LabNotClean = true;
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

    private static bool TryFaultId(JsonElement payload, out string faultId)
    {
        faultId = string.Empty;
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(FaultIdField, out JsonElement id)
            && id.ValueKind == JsonValueKind.String
            && id.GetString() is { Length: > 0 } text)
        {
            faultId = text;
            return true;
        }

        return false;
    }

    private static bool NoFaultsActive(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("faults", out JsonElement faults)
        && faults.ValueKind == JsonValueKind.Array
        && faults.GetArrayLength() == 0;

    /// <summary>
    /// A step the runner builds itself (fault inject, heal, reset, active check). It has no capture and no expected error.
    /// </summary>
    private static UatStep Synthetic(string tool, JsonElement? args) =>
        new(null, tool, args, new Dictionary<string, string>(StringComparer.Ordinal), null, null);

    private static StepOutcome Failed(string stepId, string code) =>
        new(false, "[" + code + "] step " + stepId + " failed", code, 0, null);

    private static UatRowResult Skipped(UatRow row, string reason)
    {
        (RowGrade grade, string text) = RowGrader.Grade(false, reason, null, Array.Empty<(bool, ClauseState)>());
        return new UatRowResult(row.Id, grade, text, Array.Empty<UatClauseResult>());
    }

    private static UatRowResult Blocked(UatRow row, string error, IReadOnlyList<UatRowViolation>? violations = null, IReadOnlyList<InvariantCheckError>? checkErrors = null)
    {
        (RowGrade grade, string text) = RowGrader.Grade(true, null, error, Array.Empty<(bool, ClauseState)>());
        return new UatRowResult(row.Id, grade, text, Array.Empty<UatClauseResult>(), Violations: violations, CheckErrors: checkErrors);
    }

    /// <summary>
    /// A fault that is in place (injected, not yet healed), with the id the fault tool returned.
    /// </summary>
    private sealed record InjectedFault(UatFault Fault, string FaultId);

    /// <summary>
    /// The faults of one row: the ones in place, and the heal times the clauses read.
    /// </summary>
    private sealed class FaultState
    {
        public FaultState(Dictionary<string, DateTimeOffset> heals) => Heals = heals;

        public List<InjectedFault> Active { get; } = [];

        public Dictionary<string, DateTimeOffset> Heals { get; }
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
            CoveredTo = runStart;
        }

        public string RunId { get; }

        public ulong Seed { get; }

        public string? LeaseId { get; }

        public bool OwnLease { get; }

        public DateTimeOffset RunStart { get; }

        public bool LeaseLost { get; set; }

        /// <summary>Set when a heal failed and faults were still in place after reset (R-REG). Every remaining row is BLOCKED.</summary>
        public bool LabNotClean { get; set; }

        /// <summary>The time the last invariant checkpoint covered up to. The next checkpoint starts here, so the windows never overlap.</summary>
        public DateTimeOffset CoveredTo { get; set; }
    }
}
