using System.Text.Json;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// The time window one checkpoint covers. <see cref="Section"/> and <see cref="Row"/> name the UAT row, or are null outside any row.
/// </summary>
public sealed record InvariantWindow(DateTimeOffset From, DateTimeOffset To, string? Section, string? Row);

/// <summary>
/// One broken invariant. <see cref="Detail"/> is redacted and at most 512 characters.
/// </summary>
public sealed record InvariantViolation(
    string InvariantId,
    string Service,
    string CheckId,
    string Detail,
    DateTimeOffset At,
    JsonElement? Evidence);

/// <summary>
/// An always-on check over a time window (C11).
/// </summary>
public interface IInvariant
{
    /// <summary>The invariant id, I1 to I7.</summary>
    string Id { get; }

    /// <summary>Checks the window; a failure result means the check could not run.</summary>
    Task<Result<IReadOnlyList<InvariantViolation>>> CheckAsync(InvariantWindow window, CancellationToken ct);
}

/// <summary>
/// A check that could not run (source down, parse error, timeout).
/// </summary>
public sealed record InvariantCheckError(string InvariantId, string Error);

/// <summary>A source an invariant did not check because it does not exist yet (C11 "not applicable"). Never an error.</summary>
public sealed record InvariantSkip(string InvariantId, string Source, string Reason, string? Row = null);

/// <summary>
/// The result of one checkpoint: every violation found, every check that could not run, and every source skipped as not applicable.
/// </summary>
public sealed record InvariantReport(
    IReadOnlyList<InvariantViolation> Violations,
    IReadOnlyList<InvariantCheckError> Errors,
    IReadOnlyList<InvariantSkip>? Skipped = null);

/// <summary>The result of a check that can skip sources.</summary>
public sealed record InvariantOutcome(IReadOnlyList<InvariantViolation> Violations, IReadOnlyList<InvariantSkip> Skipped);

/// <summary>An invariant that reports skipped sources; the monitor calls this instead of <see cref="IInvariant.CheckAsync"/>.</summary>
public interface ISkippableInvariant : IInvariant
{
    Task<Result<InvariantOutcome>> CheckWithSkipsAsync(InvariantWindow window, CancellationToken ct);
}

/// <summary>Whether a lab database can be checked yet.</summary>
public enum DbApplicability
{
    Applicable,
    StackNotUp,
    SchemaNotReady,
}

/// <summary>Answers whether <paramref name="database"/> can be checked. A Failure is a check error.</summary>
public delegate Task<Result<DbApplicability>> InvariantDbGate(string database, CancellationToken ct);
