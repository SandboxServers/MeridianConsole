using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Lab.Secrets;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// The result of one clause (C8): its state, the observed value after redaction, how many checks ran, when the last check ran
/// and the error text when the state is <see cref="ClauseState.Unverified"/>.
/// </summary>
public sealed record ClauseEvaluation(ClauseState State, JsonElement? Observed, int Attempts, DateTimeOffset CheckedAt, string? Error);

/// <summary>
/// Evaluates one <c>[[row.expect]]</c> clause (C7, C8). A check reads the source, resolves the pointer and applies the op. A clause
/// with <c>timeout_ms</c> polls every <c>poll_ms</c> until it holds or the timeout passes. A source error is
/// <see cref="ClauseState.Unverified"/> at once and is never polled; missing evidence and a not-held result are polled.
/// Observed values and error text pass through <see cref="Redactor"/> before they leave.
/// </summary>
public sealed class ClauseEvaluator
{
    private readonly IReadOnlyDictionary<string, IClauseSource> _sources;
    private readonly Redactor _redactor;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the evaluator over the registered sources.
    /// </summary>
    public ClauseEvaluator(IEnumerable<IClauseSource> sources, Redactor redactor, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToDictionary(source => source.Name, StringComparer.Ordinal);
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Reads the clause source before the row's first step, for a <c>delta</c> clause. The runner keeps the result and passes it
    /// to <see cref="EvaluateAsync"/>.
    /// </summary>
    public Task<Result<JsonElement>> CaptureBaselineAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clause);
        return ReadSourceAsync(clause, vars, window, ct);
    }

    /// <summary>
    /// Evaluates the clause, polling while it is not held and the timeout has not passed. <paramref name="baseline"/> is the
    /// result of <see cref="CaptureBaselineAsync"/> and is used only by <c>delta</c>.
    /// </summary>
    public async Task<ClauseEvaluation> EvaluateAsync(
        UatClause clause,
        UatVars vars,
        ClauseWindow window,
        Result<JsonElement>? baseline,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clause);
        ArgumentNullException.ThrowIfNull(vars);
        ArgumentNullException.ThrowIfNull(window);

        DateTimeOffset started = _time.GetUtcNow();
        if (clause.Source == "human")
        {
            return new ClauseEvaluation(ClauseState.Pending, null, 0, started, null);
        }

        int attempts = 0;
        while (true)
        {
            attempts++;
            Attempt attempt = await AttemptAsync(clause, vars, window, baseline, ct);
            DateTimeOffset checkedAt = _time.GetUtcNow();

            TimeSpan elapsed = checkedAt - started;
            bool timedOut = clause.TimeoutMs <= 0 || elapsed >= TimeSpan.FromMilliseconds(clause.TimeoutMs);
            if (attempt.State == ClauseState.Held || !attempt.Retry || timedOut)
            {
                return new ClauseEvaluation(
                    attempt.State,
                    attempt.Observed is null ? null : _redactor.Redact(attempt.Observed.Value),
                    attempts,
                    checkedAt,
                    attempt.Error is null ? null : _redactor.Redact(attempt.Error));
            }

            TimeSpan remaining = TimeSpan.FromMilliseconds(clause.TimeoutMs) - elapsed;
            TimeSpan poll = TimeSpan.FromMilliseconds(Math.Max(1, clause.PollMs));
            await Task.Delay(poll < remaining ? poll : remaining, _time, ct);
        }
    }

    private async Task<Attempt> AttemptAsync(
        UatClause clause,
        UatVars vars,
        ClauseWindow window,
        Result<JsonElement>? baseline,
        CancellationToken ct)
    {
        Result<JsonElement> read = await ReadSourceAsync(clause, vars, window, ct);
        if (read.IsFailure)
        {
            return Attempt.Stop(read.Error);
        }

        Result<PointerLookup> found = JsonPointer.Resolve(read.Value, clause.Pointer ?? string.Empty);
        if (found.IsFailure)
        {
            return Attempt.Stop(found.Error);
        }

        JsonElement? value = null;
        if (clause.Value is JsonElement rawValue)
        {
            Result<JsonElement> expanded = vars.Expand(rawValue);
            if (expanded.IsFailure)
            {
                return Attempt.Stop(expanded.Error);
            }

            value = expanded.Value;
        }

        JsonElement? before = null;
        if (clause.Op == "delta")
        {
            if (baseline is null)
            {
                return Attempt.Stop("baseline not captured");
            }

            if (baseline.Value.IsFailure)
            {
                return Attempt.Stop("baseline unavailable: " + baseline.Value.Error);
            }

            Result<PointerLookup> start = JsonPointer.Resolve(baseline.Value.Value, clause.Pointer ?? string.Empty);
            if (start.IsFailure)
            {
                return Attempt.Stop(start.Error);
            }

            before = start.Value.Found ? start.Value.Value : null;
        }

        JsonElement? target = found.Value.Found ? found.Value.Value : null;
        OpOutcome outcome = ClauseOps.Evaluate(clause.Op, target, value, before);
        bool missing = target is null && clause.Op != "absent";
        return new Attempt(outcome.State, target, outcome.Error, outcome.State == ClauseState.NotHeld || missing);
    }

    private Task<Result<JsonElement>> ReadSourceAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct)
    {
        if (!_sources.TryGetValue(clause.Source, out IClauseSource? source))
        {
            string error = clause.Source == "invariant"
                ? "invariants arrive in L21"
                : "unknown source " + clause.Source;
            return Task.FromResult(Result<JsonElement>.Failure(error));
        }

        Result<DateTimeOffset> since = window.SinceFor(clause.Since);
        if (since.IsFailure)
        {
            return Task.FromResult(Result<JsonElement>.Failure(since.Error));
        }

        return source.ReadAsync(clause, vars, window, ct);
    }

    /// <summary>One check: the state, the pointed value, the error text and whether a poll may try again.</summary>
    private sealed record Attempt(ClauseState State, JsonElement? Observed, string? Error, bool Retry)
    {
        public static Attempt Stop(string error) => new(ClauseState.Unverified, null, error, false);
    }
}
