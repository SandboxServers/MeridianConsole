using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dhadgar.Lab.Leasing;
using Dhadgar.Lab.Observe;
using Dhadgar.Lab.Random;
using Dhadgar.Lab.Results;
using Dhadgar.Lab.Tools;
using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhadgar.Lab.Twins;

/// <summary>
/// The outcome of one twin run (C19). <see cref="Held"/> is null when the precondition failed (a base row was not PASS): the relation is then
/// incomplete, never held. <see cref="TwinRow"/> is the synthetic <c>twin/&lt;id&gt;</c> row, which both bundles carry.
/// </summary>
public sealed record TwinRunResult(string ExperimentId, bool? Held, RunOverall Overall, UatRowResult TwinRow, UatRunResult Base, UatRunResult Variant, IReadOnlyList<TwinDiff> Diffs);

/// <summary>
/// Runs one twin experiment (L53, C19): the base world (<c>stack_reset_data</c>, then the base rows with <c>Pair(role = base)</c>), then the
/// variant world (<c>stack_reset_data</c>, then the variant rows with the overlay and <c>Pair(role = variant)</c>). The relation is compared only
/// when every base row is PASS. The synthetic <c>twin/&lt;id&gt;</c> row is written to both bundles, which are finished only after it exists.
/// </summary>
public sealed class TwinRunner
{
    /// <summary>The budget of the heal that runs after the variant world, whether or not the run was cancelled.</summary>
    internal static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(30);

    private const string OwnLeasePrefix = "twin-run/";
    private const string DefaultPurpose = "twin";

    private readonly UatRunner _runner;
    private readonly LabToolDispatcher _dispatcher;
    private readonly LeaseBook _leases;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    /// <summary>Creates the runner over the UAT runner that runs both worlds.</summary>
    public TwinRunner(UatRunner runner, LabToolDispatcher dispatcher, LeaseBook leases, TimeProvider time, ILogger? logger = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Runs one experiment. Never throws for run failures. A world that fails to start returns a failure; the sinks then hold no finished bundle.
    /// </summary>
    /// <param name="spec">The parsed experiment.</param>
    /// <param name="baseSpec">The base catalog spec, loaded and validated.</param>
    /// <param name="variantSpec">The variant catalog spec, loaded and validated.</param>
    /// <param name="baseSink">Receives the base bundle.</param>
    /// <param name="variantSink">Receives the variant bundle.</param>
    /// <param name="seed">The call's seed, or null for the experiment's seed, or a fresh seed.</param>
    /// <param name="leaseId">The lease both worlds run under, or null to take and release an own lease.</param>
    /// <param name="label">The run label, or null.</param>
    /// <param name="ct">Cancels the run.</param>
    public async Task<Result<TwinRunResult>> RunAsync(TwinSpec spec, UatSpec baseSpec, UatSpec variantSpec, IUatRunSink baseSink, IUatRunSink variantSink,
        ulong? seed, string? leaseId, string? label, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(baseSpec);
        ArgumentNullException.ThrowIfNull(variantSpec);
        ArgumentNullException.ThrowIfNull(baseSink);
        ArgumentNullException.ThrowIfNull(variantSink);

        if (!string.Equals(Path.GetFileName(spec.Base.Spec), Path.GetFileName(baseSpec.File), StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(spec.Variant.Spec), Path.GetFileName(variantSpec.File), StringComparison.Ordinal))
        {
            return Result<TwinRunResult>.Failure("[Lab.InvalidArgument] the experiment's specs are not the specs given");
        }

        Result baseRows = TwinSpecLoader.CheckBaseRows(spec, baseSpec.Rows.Select(r => r.Id).ToArray());
        if (baseRows.IsFailure)
        {
            return Result<TwinRunResult>.Failure(baseRows.Error);
        }

        ulong effectiveSeed = seed ?? spec.Seed ?? LabRandom.NewSeed();
        string twinRunId = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        string? ownLease = null;
        if (leaseId is null)
        {
            Result<LeaseGrant> grant = _leases.Acquire(OwnLeasePrefix + twinRunId, string.IsNullOrWhiteSpace(label) ? DefaultPurpose : label, UatRunner.OwnLeaseTtlSeconds, false, null);
            if (grant.IsFailure)
            {
                return Result<TwinRunResult>.Failure(grant.Error);
            }

            ownLease = grant.Value!.LeaseId;
            leaseId = ownLease;
        }

        try
        {
            var deferredBase = new DeferredSink(baseSink);
            var deferredVariant = new DeferredSink(variantSink);
            string relation = RelationText(spec.Relation);

            Result reset = await ResetAsync(spec, leaseId, ct).ConfigureAwait(false);
            if (reset.IsFailure)
            {
                return Result<TwinRunResult>.Failure(reset.Error);
            }

            var baseOverlay = new UatRunOverlay(Array.Empty<FaultSpec>(), null, Array.Empty<string>(), new PairInfo(spec.Id, "base", string.Empty, relation));
            Result<UatRunResult> baseRun = await _runner.RunAsync(
                new UatRunRequest(baseSpec, spec.Base.Rows, effectiveSeed, leaseId, label, PlanOnly: false), deferredBase, baseOverlay, ct).ConfigureAwait(false);
            if (baseRun.IsFailure)
            {
                return Result<TwinRunResult>.Failure(baseRun.Error);
            }

            reset = await ResetAsync(spec, leaseId, ct).ConfigureAwait(false);
            if (reset.IsFailure)
            {
                return Result<TwinRunResult>.Failure(reset.Error);
            }

            var variantOverlay = new UatRunOverlay(
                spec.Variant.Faults,
                spec.Pre?.Spec,
                spec.Pre?.Rows ?? Array.Empty<string>(),
                new PairInfo(spec.Id, "variant", baseRun.Value!.RunId, relation));
            Result<UatRunResult> variantRun = await _runner.RunAsync(
                new UatRunRequest(variantSpec, spec.Variant.Rows, effectiveSeed, leaseId, label, PlanOnly: false), deferredVariant, variantOverlay, ct).ConfigureAwait(false);
            if (variantRun.IsFailure)
            {
                return Result<TwinRunResult>.Failure(variantRun.Error);
            }

            Result<Verdict> verdict = Judge(spec, baseRun.Value!, variantRun.Value!);
            if (verdict.IsFailure)
            {
                return Result<TwinRunResult>.Failure(verdict.Error);
            }

            // R3-N1 (C12): one twin signature per pointer that did not hold; nothing when the relation held.
            UatRowResult twinRow = new(
                "twin/" + spec.Id,
                verdict.Value!.Grade,
                verdict.Value.Reason,
                Array.Empty<UatClauseResult>(),
                TwinPointers: verdict.Value.Held == false
                    ? verdict.Value.Diffs.Select(d => d.Locator).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                    : null);

            UatRunResult baseFinal = Finish(deferredBase.Result!, baseSpec, twinRow, baseRun.Value!.Pair! with { PartnerRunId = variantRun.Value!.RunId });
            UatRunResult variantFinal = Finish(deferredVariant.Result!, variantSpec, twinRow, variantRun.Value!.Pair);

            baseSink.RowFinished(twinRow);
            baseSink.RunFinished(baseFinal);
            variantSink.RowFinished(twinRow);
            variantSink.RunFinished(variantFinal);

            return Result<TwinRunResult>.Success(new TwinRunResult(
                spec.Id, verdict.Value.Held, variantFinal.Overall, twinRow, baseFinal, variantFinal, verdict.Value.Diffs));
        }
        finally
        {
            if (spec.Variant.Faults.Count > 0)
            {
                await HealAsync(leaseId).ConfigureAwait(false);
            }

            if (ownLease is not null)
            {
                _ = _leases.Release(ownLease);
            }
        }
    }

    /// <summary>The verdict of the relation: the held value, its diffs, and the synthetic row's grade and reason.</summary>
    private sealed record Verdict(bool? Held, RowGrade Grade, string Reason, IReadOnlyList<TwinDiff> Diffs);

    /// <summary>
    /// Applies the precondition (every base row PASS), then compares the two projections (C19). A failed precondition is SKIPPED with
    /// <c>held = null</c>, never held.
    /// </summary>
    private static Result<Verdict> Judge(TwinSpec spec, UatRunResult baseRun, UatRunResult variantRun)
    {
        UatRowResult? notPassed = baseRun.Rows.FirstOrDefault(r => r.Grade != RowGrade.Pass);
        if (notPassed is not null)
        {
            return Result<Verdict>.Success(new Verdict(null, RowGrade.Skipped, $"twin precondition: {notPassed.RowId} {notPassed.Grade}", Array.Empty<TwinDiff>()));
        }

        JsonObject baseProjection = RunProjection.Build(baseRun.Rows);
        JsonObject variantProjection = RunProjection.Build(variantRun.Rows);
        Result<TwinComparison> compared = TwinComparer.Compare(
            spec, JsonSerializer.SerializeToElement(baseProjection), JsonSerializer.SerializeToElement(variantProjection));
        if (compared.IsFailure)
        {
            return Result<Verdict>.Failure(compared.Error);
        }

        TwinComparison comparison = compared.Value!;
        return comparison.Held
            ? Result<Verdict>.Success(new Verdict(true, RowGrade.Pass, "relation held", comparison.Diffs))
            : Result<Verdict>.Success(new Verdict(false, RowGrade.Fail, $"relation not held: {comparison.Diffs.Count} difference(s)", comparison.Diffs));
    }

    /// <summary>
    /// The finished result of one world: its rows plus the synthetic row, the overall recomputed over them (D-ML89), and the pair as given.
    /// </summary>
    private static UatRunResult Finish(UatRunResult run, UatSpec spec, UatRowResult twinRow, PairInfo? pair)
    {
        IReadOnlyList<UatRowResult> rows = run.Rows.Append(twinRow).ToArray();
        var graded = rows.Select(row => new GradedRow(spec.Section.Id, row.RowId, row.Grade)).ToList();
        RunOverall overall = RunSummary.Compute(graded, new Dictionary<string, KnownMatch>(StringComparer.Ordinal)).Overall;
        return run with { Rows = rows, Overall = overall, Pair = pair };
    }

    /// <summary>Runs <c>stack_reset_data</c> under the lease: each world starts from an empty data set.</summary>
    private async Task<Result> ResetAsync(TwinSpec spec, string leaseId, CancellationToken ct)
    {
        var args = new JsonObject { [LabToolDispatcher.LeaseIdArgument] = leaseId };
        var context = new LabCallContext(Guid.NewGuid().ToString("N"), leaseId, "twin:" + spec.Id);
        LabToolResult call = await _dispatcher.InvokeAsync("stack_reset_data", JsonSerializer.SerializeToElement(args), context, ct).ConfigureAwait(false);
        if (!call.IsError)
        {
            return Result.Success();
        }

        // R3-N5: name the cause. The dispatcher has already redacted the payload; the text is cut so a long error stays readable.
        string detail = ErrorDetail(call.Payload);
        return Result.Failure("[Lab.Unavailable] stack_reset_data failed: " + (detail.Length > 300 ? detail[..300] : detail));
    }

    /// <summary>
    /// The error text of a failed tool payload. A payload that is not a JSON object, or an <c>error</c> that is not an object, yields a
    /// fixed text rather than an exception (N5, pass 2), so the twin run still returns its <see cref="Result"/>.
    /// </summary>
    internal static string ErrorDetail(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return "the tool returned no error object";
        }

        if (payload.TryGetProperty("error", out JsonElement error)
            && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message", out JsonElement message)
            && message.ValueKind == JsonValueKind.String
            && message.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        return payload.GetRawText();
    }

    /// <summary>
    /// Heals the variant's faults after the variant world, under its own 30 s budget (the run's token may already be cancelled). A failed heal
    /// is not reported here: the lease end and the fault book backstop it.
    /// </summary>
    private async Task HealAsync(string leaseId)
    {
        using var budget = new CancellationTokenSource(CleanupBudget, _time);
        var args = new JsonObject { [LabToolDispatcher.LeaseIdArgument] = leaseId };
        var context = new LabCallContext(Guid.NewGuid().ToString("N"), leaseId, "twin:heal");
        try
        {
            _ = await _dispatcher.InvokeAsync("fault_reset", JsonSerializer.SerializeToElement(args), context, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            // Budget spent; the lease end and the fault book's deadline remain.
        }
        catch (Exception)
        {
            // R3-N5 (S-N5): a heal that throws must not hide the variant's result. Log the tool name only, never arguments.
            _logger.LogWarning("twin heal did not finish: {Tool}", "fault_reset");
        }
    }

    /// <summary>The C19 text of a relation, as the bundle's <c>pair.relation</c> and the synthetic row carry it.</summary>
    internal static string RelationText(TwinRelation relation) => relation switch
    {
        TwinRelation.EqualOutcome => "equal_outcome",
        TwinRelation.Isolated => "isolated",
        _ => throw new ArgumentOutOfRangeException(nameof(relation)),
    };

    /// <summary>
    /// Holds a finished run until the twin row exists. Every other call passes through to the bundle writer, so the folder name and the
    /// progress are the writer's own.
    /// </summary>
    private sealed class DeferredSink(IUatRunSink inner) : IUatRunSink
    {
        public UatRunResult? Result { get; private set; }

        public string? FolderName => inner.FolderName;

        public string? FinalOverall => inner.FinalOverall;

        public void RunStarted(string runId, ulong seed, UatSpec spec) => inner.RunStarted(runId, seed, spec);

        public void RowFinished(UatRowResult row) => inner.RowFinished(row);

        public void RunFinished(UatRunResult result) => Result = result;
    }
}
