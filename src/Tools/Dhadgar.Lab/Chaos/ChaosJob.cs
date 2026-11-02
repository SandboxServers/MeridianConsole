using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Agent.Core.Files;
using Dhadgar.Lab.Jobs;
using Dhadgar.Lab.Tools;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Chaos;

/// <summary>
/// A chaos run as prepared before its job starts: the validated plan, the seed, the run length and the seeded timeline.
/// </summary>
/// <param name="Plan">The validated plan.</param>
/// <param name="Seed">The run seed; the same seed gives the same timeline (R-SEED).</param>
/// <param name="DurationS">The run length in seconds.</param>
/// <param name="Schedule">The timeline from <see cref="ChaosPlan.Schedule"/>.</param>
public sealed record ChaosRun(ChaosPlan Plan, ulong Seed, int DurationS, IReadOnlyList<ChaosScheduleEntry> Schedule);

/// <summary>
/// The chaos job (<c>chaos_run</c>, L29). It loops the plan's workload through <c>uat_run</c>, paced by the Gateway pacer
/// (D-ML88), and injects the seeded faults through the C4 fault tools only (R-BLAST). It checkpoints the always-on
/// invariants every 30 seconds, and it heals every fault it started before it returns, also when the job is cancelled
/// (A2): the heal runs under its own 30 second budget, not the job's token. The timeline is appended to
/// <c>repro.toml</c> in the job folder as it happens.
/// </summary>
public static class ChaosJob
{
    /// <summary>The job kind, also the bundle kind.</summary>
    public const string Kind = "chaos";

    /// <summary>The timeline file in the job folder.</summary>
    public const string ReproFile = "repro.toml";

    /// <summary>The time between two invariant checkpoints.</summary>
    public static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(30);

    /// <summary>The budget for the heals in the job's <c>finally</c>, on its own clock and token.</summary>
    public static readonly TimeSpan HealBudget = TimeSpan.FromSeconds(30);

    /// <summary>The pause after a failed workload run, before the next one.</summary>
    public static readonly TimeSpan WorkloadRetryDelay = TimeSpan.FromSeconds(5);

    private const string SpecsPrefix = "docs/guides/uat-specs/";

    /// <summary>
    /// Builds the run: the plan's seeded timeline for <paramref name="durationS"/> seconds.
    /// </summary>
    /// <param name="plan">The validated plan.</param>
    /// <param name="seed">The run seed.</param>
    /// <param name="durationS">The run length in seconds.</param>
    public static ChaosRun Prepare(ChaosPlan plan, ulong seed, int durationS)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new ChaosRun(plan, seed, durationS, plan.Schedule(seed, durationS));
    }

    /// <summary>
    /// The job work. Runs the workload and the timeline until the run length has passed or the job is cancelled. Every fault
    /// this job started is healed in <c>finally</c>. Each tool call carries the job's own lease id as <c>lease_id</c>; the
    /// id is never logged or written.
    /// </summary>
    /// <param name="run">The prepared run.</param>
    /// <param name="context">The job context.</param>
    /// <param name="invoke">The tool call: the dispatcher's <c>InvokeAsync</c> in the daemon, a fake in tests.</param>
    /// <param name="pacer">The Gateway pacer, awaited before each workload run.</param>
    /// <param name="time">The clock.</param>
    /// <param name="validator">The path validator.</param>
    /// <param name="ct">The job's cancellation token.</param>
    public static async Task<Result<JobOutcome>> RunAsync(ChaosRun run, JobContext context,
        Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke,
        GatewayPacer pacer, TimeProvider time, IPathValidator validator, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);
        ArgumentNullException.ThrowIfNull(pacer);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(validator);

        Result<string> repro = validator.ValidatePath(Path.Combine(context.Folder, ReproFile), [context.Folder]);
        if (repro.IsFailure)
        {
            return Result<JobOutcome>.Failure("[Lab.InvalidArgument] the job folder is outside the lab jobs root");
        }

        var state = new RunState(run, context, invoke, pacer, time, repro.Value!);
        Result header = state.WriteHeader();
        if (header.IsFailure)
        {
            return Result<JobOutcome>.Failure(header.Error);
        }

        try
        {
            foreach (ChaosScheduleEntry entry in run.Schedule)
            {
                await state.RunUntilAsync(state.Origin + TimeSpan.FromSeconds(entry.StartS), ct).ConfigureAwait(false);
                await state.StartFaultAsync(entry, ct).ConfigureAwait(false);
            }

            await state.RunUntilAsync(state.Origin + TimeSpan.FromSeconds(run.DurationS), ct).ConfigureAwait(false);
        }
        finally
        {
            // A2: the heal has its own budget and its own token, so a cancelled job still heals what it started.
            using var budget = new CancellationTokenSource(HealBudget, time);
            await state.HealAllAsync(budget.Token).ConfigureAwait(false);
        }

        return Result<JobOutcome>.Success(state.Outcome());
    }

    /// <summary>The spec argument of <c>uat_run</c>: the path below <c>docs/guides/uat-specs/</c>, which the tool takes.</summary>
    internal static string SpecArgument(string spec) =>
        spec.StartsWith(SpecsPrefix, StringComparison.Ordinal) ? spec[SpecsPrefix.Length..] : spec;

    /// <summary>A fault this job started and has not yet healed.</summary>
    private sealed record ActiveFault(string FaultId, DateTimeOffset HealAt);

    /// <summary>The mutable state of one run. Not shared between jobs.</summary>
    private sealed class RunState
    {
        private readonly ChaosRun _run;
        private readonly JobContext _context;
        private readonly Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> _invoke;
        private readonly GatewayPacer _pacer;
        private readonly TimeProvider _time;
        private readonly string _reproPath;
        private readonly List<ActiveFault> _active = [];
        private readonly DateTimeOffset _origin;
        private DateTimeOffset _nextCheckpoint;
        private int _faultsStarted;
        private int _faultsRefused;
        private int _faultsHealed;
        private int _healFailed;
        private int _workloadLoops;
        private int _workloadErrors;
        private int _checkpoints;
        private int _violations;
        private int _checkErrors;
        private bool _timelineFailed;

        public RunState(ChaosRun run, JobContext context,
            Func<string, JsonElement, LabCallContext, CancellationToken, Task<LabToolResult>> invoke,
            GatewayPacer pacer, TimeProvider time, string reproPath)
        {
            _run = run;
            _context = context;
            _invoke = invoke;
            _pacer = pacer;
            _time = time;
            _reproPath = reproPath;
            _origin = time.GetUtcNow();
            _nextCheckpoint = _origin + CheckpointInterval;
        }

        /// <summary>The run's start: the schedule's seconds count from here.</summary>
        public DateTimeOffset Origin => _origin;

        /// <summary>Writes the header of the timeline file. The seed is written as a string: it is an unsigned 64-bit value.</summary>
        public Result WriteHeader()
        {
            var text = new StringBuilder();
            text.Append("schema = 1\n");
            text.Append("plan = ").Append(Quote(_run.Plan.Id)).Append('\n');
            text.Append("seed = ").Append(Quote(_run.Seed.ToString(CultureInfo.InvariantCulture))).Append('\n');
            text.Append("duration_s = ").Append(_run.DurationS.ToString(CultureInfo.InvariantCulture)).Append('\n');
            try
            {
                File.WriteAllText(_reproPath, text.ToString());
                return Result.Success();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Failure("[Lab.IoError] the chaos timeline could not be written");
            }
        }

        /// <summary>
        /// Runs checkpoints, due heals and workload runs until <paramref name="until"/>. Each step checks the token first.
        /// </summary>
        public async Task RunUntilAsync(DateTimeOffset until, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                DateTimeOffset now = _time.GetUtcNow();
                if (now >= until)
                {
                    return;
                }

                if (now >= _nextCheckpoint)
                {
                    _nextCheckpoint = now + CheckpointInterval;
                    await CheckpointAsync(ct).ConfigureAwait(false);
                    continue;
                }

                ActiveFault? due = _active.FirstOrDefault(fault => fault.HealAt <= now);
                if (due is not null)
                {
                    await HealOneAsync(due, now, ct).ConfigureAwait(false);
                    continue;
                }

                await WorkloadOnceAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>Starts one scheduled fault. At the concurrency limit the earliest active fault is healed first.</summary>
        public async Task StartFaultAsync(ChaosScheduleEntry entry, CancellationToken ct)
        {
            if (_active.Count >= _run.Plan.MaxConcurrent)
            {
                ActiveFault earliest = _active.OrderBy(fault => fault.HealAt).First();
                await HealOneAsync(earliest, _time.GetUtcNow(), ct).ConfigureAwait(false);
            }

            ChaosFault fault = _run.Plan.Faults[entry.FaultIndex];
            var args = (JsonObject)fault.Args.DeepClone();
            args["heal_after_s"] = JsonValue.Create(entry.DurationS);
            args["lease_id"] = JsonValue.Create(_context.LeaseId);
            LabToolResult call = await _invoke(fault.Tool, JsonSerializer.SerializeToElement(args), CallContext(), ct)
                .ConfigureAwait(false);
            string? faultId = call.IsError ? null : StringOf(call.Payload, "fault_id");
            int at = Seconds(_time.GetUtcNow(), _origin);
            if (faultId is null)
            {
                _faultsRefused++;
                Timeline(at, "refused", fault.Tool, null, entry.DurationS);
                return;
            }

            _faultsStarted++;
            _active.Add(new ActiveFault(faultId, _time.GetUtcNow() + TimeSpan.FromSeconds(entry.DurationS)));
            Timeline(at, "start", fault.Tool, faultId, entry.DurationS);
        }

        /// <summary>Heals every fault still active. Runs in the job's <c>finally</c> under the heal budget's token.</summary>
        public async Task HealAllAsync(CancellationToken budget)
        {
            foreach (ActiveFault fault in _active.ToList())
            {
                try
                {
                    await HealOneAsync(fault, _time.GetUtcNow(), budget).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (budget.IsCancellationRequested)
                {
                    // The heal budget is spent; FaultBook heals what is left after heal_after_s.
                    return;
                }
                catch (Exception)
                {
                    // R2-N12 and S-N5: one heal that throws must not skip the heals after it. The timeline records the
                    // failure with the tool name only; the exception text is not written anywhere.
                    _healFailed++;
                    Timeline(Seconds(_time.GetUtcNow(), _origin), "heal_failed", "fault_heal", fault.FaultId, 0);
                }
            }
        }

        /// <summary>
        /// The job outcome: <c>pass</c> only when at least one workload loop succeeded, no invariant was violated, no check
        /// failed and the timeline was written. A run with no successful loop (none, or every loop errored) fails, with a reason.
        /// </summary>
        public JobOutcome Outcome()
        {
            string overall = _workloadLoops > 0 && _violations == 0 && _checkErrors == 0 && !_timelineFailed ? "pass" : "fail";
            var result = new JsonObject
            {
                ["plan"] = _run.Plan.Id,
                ["seed"] = _run.Seed.ToString(CultureInfo.InvariantCulture),
                ["duration_s"] = _run.DurationS,
                ["faults_started"] = _faultsStarted,
                ["faults_refused"] = _faultsRefused,
                ["faults_healed"] = _faultsHealed,
                ["heal_failed"] = _healFailed,
                ["workload_loops"] = _workloadLoops,
                ["workload_errors"] = _workloadErrors,
                ["checkpoints"] = _checkpoints,
                ["violations"] = _violations,
                ["check_errors"] = _checkErrors,
                ["reason"] = _workloadLoops == 0 ? "no workload loop succeeded" : null,
            };
            return new JobOutcome(overall, result);
        }

        private async Task WorkloadOnceAsync(CancellationToken ct)
        {
            Result paced = await _pacer.WaitAsync(ct).ConfigureAwait(false);
            if (paced.IsFailure)
            {
                _workloadErrors++;
                await Task.Delay(WorkloadRetryDelay, _time, ct).ConfigureAwait(false);
                return;
            }

            var args = JsonSerializer.SerializeToElement(new
            {
                spec = SpecArgument(_run.Plan.Workload.Spec),
                rows = _run.Plan.Workload.Rows,
                seed = _run.Seed,
                lease_id = _context.LeaseId,
            });
            LabToolResult call = await _invoke("uat_run", args, CallContext(), ct).ConfigureAwait(false);
            if (call.IsError)
            {
                _workloadErrors++;
                await Task.Delay(WorkloadRetryDelay, _time, ct).ConfigureAwait(false);
                return;
            }

            _workloadLoops++;
            _context.Report("workload loop " + _workloadLoops.ToString(CultureInfo.InvariantCulture) + ", faults active " + _active.Count.ToString(CultureInfo.InvariantCulture));
        }

        private async Task CheckpointAsync(CancellationToken ct)
        {
            LabToolResult call = await _invoke("invariants_check", JsonSerializer.SerializeToElement(new { }), CallContext(), ct)
                .ConfigureAwait(false);
            _checkpoints++;
            if (call.IsError)
            {
                _checkErrors++;
                return;
            }

            _violations += IntOf(call.Payload, "count");
            _checkErrors += ArrayLengthOf(call.Payload, "errors");
        }

        private async Task HealOneAsync(ActiveFault fault, DateTimeOffset now, CancellationToken ct)
        {
            _active.Remove(fault);
            var args = JsonSerializer.SerializeToElement(new { fault_id = fault.FaultId, lease_id = _context.LeaseId });
            LabToolResult call = await _invoke("fault_heal", args, CallContext(), ct).ConfigureAwait(false);
            if (!call.IsError)
            {
                _faultsHealed++;
            }

            Timeline(Seconds(now, _origin), call.IsError ? "heal_failed" : "heal", "fault_heal", fault.FaultId, 0);
        }

        private LabCallContext CallContext() => new(Guid.NewGuid().ToString("N"), _context.LeaseId, "job:" + _context.JobId);

        /// <summary>Appends one timeline entry. A write failure marks the outcome failed; it does not stop the run.</summary>
        private void Timeline(int atS, string kind, string tool, string? faultId, int durationS)
        {
            var text = new StringBuilder();
            text.Append("\n[[timeline]]\n");
            text.Append("at_s = ").Append(atS.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("event = ").Append(Quote(kind)).Append('\n');
            text.Append("tool = ").Append(Quote(tool)).Append('\n');
            if (faultId is not null)
            {
                text.Append("fault_id = ").Append(Quote(faultId)).Append('\n');
            }

            text.Append("duration_s = ").Append(durationS.ToString(CultureInfo.InvariantCulture)).Append('\n');
            try
            {
                File.AppendAllText(_reproPath, text.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _timelineFailed = true;
            }
        }

        private static int Seconds(DateTimeOffset at, DateTimeOffset origin) => (int)(at - origin).TotalSeconds;

        private static string Quote(string value) =>
            "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

        private static string? StringOf(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int IntOf(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number)
                ? number
                : 0;

        private static int ArrayLengthOf(JsonElement payload, string name) =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
                ? value.GetArrayLength()
                : 0;
    }
}
