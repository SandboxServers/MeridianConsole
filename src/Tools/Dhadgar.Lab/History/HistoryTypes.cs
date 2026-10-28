using System.Text.Json;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.History;

/// <summary>
/// One operation of a history (C19), built from the C18 observation lines of an <c>Invoke</c>/<c>Complete</c> pair.
/// </summary>
/// <param name="OpId">The op id shared by the invoke and complete lines.</param>
/// <param name="Actor">The logical actor that issued the op.</param>
/// <param name="Key">The key the op acts on (for enrollment, the token id).</param>
/// <param name="F">The operation name, e.g. <c>enroll</c>.</param>
/// <param name="Input">The redacted input of the op.</param>
/// <param name="InvokeSeq">The sequence number of the invoke line.</param>
/// <param name="CompleteSeq">The sequence number of the complete line; <c>null</c> when the op is <c>Unknown</c> (its return is +infinity).</param>
/// <param name="Outcome">The outcome: <c>Ok</c>, <c>Fail</c> or <c>Unknown</c>.</param>
/// <param name="Output">The redacted result of the complete line.</param>
public sealed record HistoryOp(
    string OpId,
    string Actor,
    string Key,
    string F,
    JsonElement Input,
    long InvokeSeq,
    long? CompleteSeq,
    OpOutcome Outcome,
    JsonElement Output);

/// <summary>
/// A recorded history of one run (C19). <see cref="FinalState"/> is the db_query snapshot taken at run end.
/// </summary>
public sealed record History(string RunId, string Scenario, IReadOnlyList<HistoryOp> Ops, JsonElement FinalState);

/// <summary>
/// The sequential specification of one object type (C19). Models are pure: no clock, no I/O.
/// </summary>
/// <typeparam name="TState">The model state; equality decides memoisation.</typeparam>
public interface IHistoryModel<TState> where TState : IEquatable<TState>
{
    /// <summary>The model name used in signatures (for example <c>enrollment</c>).</summary>
    string Name { get; }

    /// <summary>The state of one key before the run's first op on it.</summary>
    TState Initial(string key, JsonElement finalStateForKey);

    /// <summary>
    /// The sequential spec: whether <paramref name="op"/> may take effect in <paramref name="s"/>, and the state after it.
    /// </summary>
    bool TryStep(TState s, HistoryOp op, out TState next);

    /// <summary>Whether the final state of the key agrees with <paramref name="s"/>.</summary>
    bool FinalMatches(TState s, JsonElement finalStateForKey);
}

/// <summary>
/// One rule violation found by a checker or rule set (C19).
/// </summary>
/// <param name="Rule">The rule id, e.g. <c>linearizability</c> or <c>E1</c>.</param>
/// <param name="OpIds">The op ids involved, in InvokeSeq order.</param>
/// <param name="Detail">A short human-readable explanation.</param>
/// <param name="Required">Whether the violation fails the row. E5 is reported with <c>false</c> until D-ML49 (C19).</param>
public sealed record HistoryViolation(string Rule, IReadOnlyList<string> OpIds, string Detail, bool Required = true);

/// <summary>
/// The outcome of a check (C19). <see cref="Verdict"/> is <c>linearizable</c>, <c>violation</c> or <c>unknown</c>.
/// </summary>
/// <param name="Verdict">The verdict string.</param>
/// <param name="Violations">The violations, empty unless the verdict is <c>violation</c>.</param>
/// <param name="Minimal">The minimised failing history, when minimisation ran.</param>
/// <param name="Reason">Why the verdict is <c>unknown</c>; <c>null</c> otherwise.</param>
public sealed record CheckResult(
    string Verdict,
    IReadOnlyList<HistoryViolation> Violations,
    History? Minimal,
    string? Reason = null);
