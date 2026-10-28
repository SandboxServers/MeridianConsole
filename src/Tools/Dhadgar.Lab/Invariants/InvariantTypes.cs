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

/// <summary>
/// The result of one checkpoint: every violation found and every check that could not run.
/// </summary>
public sealed record InvariantReport(IReadOnlyList<InvariantViolation> Violations, IReadOnlyList<InvariantCheckError> Errors);
