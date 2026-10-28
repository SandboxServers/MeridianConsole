using System.Text.Json;
using Dhadgar.Lab.Uat.Spec;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Uat.Eval;

/// <summary>
/// One evidence source of a clause (C7 table). A failure is a result, never an exception: the evaluator turns it into
/// <see cref="ClauseState.Unverified"/> with the error text.
/// </summary>
public interface IClauseSource
{
    /// <summary>The C7 source name, for example <c>db</c>.</summary>
    string Name { get; }

    /// <summary>Reads the evidence for one clause. The returned element is the object the pointer and op apply to.</summary>
    Task<Result<JsonElement>> ReadAsync(UatClause clause, UatVars vars, ClauseWindow window, CancellationToken ct);
}

/// <summary>
/// The timing facts a clause is evaluated against: the run and row starts, step starts, fault heal moments and step outcomes.
/// </summary>
public sealed record ClauseWindow(
    DateTimeOffset RunStart,
    DateTimeOffset RowStart,
    IReadOnlyDictionary<string, DateTimeOffset> StepStarts,
    IReadOnlyDictionary<string, DateTimeOffset> FaultHeals,
    IReadOnlyDictionary<string, StepOutcome> Steps)
{
    /// <summary>
    /// Resolves a C7 <c>since</c> value to a moment. <c>fault_heal:&lt;id&gt;</c> for a fault that has not healed is a failure.
    /// </summary>
    public Result<DateTimeOffset> SinceFor(string since)
    {
        ArgumentNullException.ThrowIfNull(since);

        if (since == "run_start")
        {
            return Result<DateTimeOffset>.Success(RunStart);
        }

        if (since is "row_start" or "")
        {
            return Result<DateTimeOffset>.Success(RowStart);
        }

        if (since.StartsWith("step:", StringComparison.Ordinal))
        {
            string id = since["step:".Length..];
            return StepStarts.TryGetValue(id, out DateTimeOffset started)
                ? Result<DateTimeOffset>.Success(started)
                : Result<DateTimeOffset>.Failure($"step {id} has not run");
        }

        if (since.StartsWith("fault_heal:", StringComparison.Ordinal))
        {
            string id = since["fault_heal:".Length..];
            return FaultHeals.TryGetValue(id, out DateTimeOffset healed)
                ? Result<DateTimeOffset>.Success(healed)
                : Result<DateTimeOffset>.Failure($"fault {id} not healed");
        }

        return Result<DateTimeOffset>.Failure("[Lab.InvalidArgument] unknown since " + since);
    }
}

/// <summary>
/// The outcome of one step, as the <c>step</c> source and <c>expect_error</c> see it (C7 <c>step</c> result shape).
/// </summary>
public sealed record StepOutcome(bool Ok, string? Error, string? ErrorCode, long DurationMs, JsonElement? Result);
