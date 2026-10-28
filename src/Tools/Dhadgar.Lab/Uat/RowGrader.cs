using Dhadgar.Lab.Invariants;

namespace Dhadgar.Lab.Uat;

/// <summary>
/// The grade of one row (C8). <see cref="Skipped"/> is a row that did not run; the others are only set once a row ran.
/// </summary>
public enum RowGrade
{
    /// <summary>Every required clause is held.</summary>
    Pass,

    /// <summary>A required clause is not held.</summary>
    Fail,

    /// <summary>A step returned an error; later steps and the clauses did not run.</summary>
    Blocked,

    /// <summary>The row did not run: filtered out, a dependency did not pass, or a capability is missing.</summary>
    Skipped,

    /// <summary>Every required clause is held or pending human attestation, and at least one is pending.</summary>
    NeedsHuman,

    /// <summary>The evidence is missing, or the row has no required clause.</summary>
    Unverified,
}

/// <summary>
/// The overall result of a run (C8). JSON values: <c>pass</c>, <c>pass_with_known</c>, <c>incomplete</c>, <c>fail</c>.
/// </summary>
public enum RunOverall
{
    /// <summary>No failure and nothing incomplete.</summary>
    Pass,

    /// <summary>No new failure, nothing incomplete, and at least one failure is linked to an open known issue.</summary>
    PassWithKnown,

    /// <summary>No new failure, but a row needs a human, was skipped, or nothing ran.</summary>
    Incomplete,

    /// <summary>At least one new failure.</summary>
    Fail,
}

/// <summary>
/// Grades one row from its run state and clause states (C8). Pure: no IO, no time.
/// </summary>
public static class RowGrader
{
    /// <summary>
    /// Grades a row. Precedence once a row ran: BLOCKED &gt; FAIL &gt; UNVERIFIED &gt; NEEDS_HUMAN &gt; PASS. Non-required clauses never change the grade.
    /// An invariant violation that is not allowed for the row is a FAIL (C8, D-ML24); an allowed one never changes the grade.
    /// A check error in the row turns a PASS, NEEDS_HUMAN or UNVERIFIED into UNVERIFIED with the reason <c>invariant &lt;id&gt; unchecked: &lt;error&gt;</c>.
    /// </summary>
    /// <param name="ran">Whether the row ran.</param>
    /// <param name="skipReason">The reason a row did not run; used when <paramref name="ran"/> is false.</param>
    /// <param name="stepError">The error of the step that stopped the row, or null.</param>
    /// <param name="clauses">Each clause as (required, state).</param>
    /// <param name="violations">Each invariant violation in the row's window as (invariant id, allowed for this row).</param>
    /// <param name="checkErrors">The invariant checks that could not run in the row's window.</param>
    /// <returns>The grade and a short reason.</returns>
    public static (RowGrade Grade, string Reason) Grade(bool ran, string? skipReason, string? stepError, IReadOnlyList<(bool Required, ClauseState State)> clauses, IReadOnlyList<(string InvariantId, bool Allowed)>? violations = null, IReadOnlyList<InvariantCheckError>? checkErrors = null)
    {
        ArgumentNullException.ThrowIfNull(clauses);

        if (!ran)
        {
            return (RowGrade.Skipped, string.IsNullOrWhiteSpace(skipReason) ? "not run" : skipReason);
        }

        if (stepError is not null)
        {
            return (RowGrade.Blocked, stepError);
        }

        int required = 0;
        bool anyNotHeld = false;
        bool anyUnverified = false;
        bool anyPending = false;
        foreach ((bool isRequired, ClauseState state) in clauses)
        {
            if (!isRequired)
            {
                continue;
            }

            required++;
            switch (state)
            {
                case ClauseState.NotHeld:
                    anyNotHeld = true;
                    break;
                case ClauseState.Unverified:
                    anyUnverified = true;
                    break;
                case ClauseState.Pending:
                    anyPending = true;
                    break;
            }
        }

        (string InvariantId, bool Allowed)? unallowed = null;
        if (violations is not null)
        {
            foreach ((string invariantId, bool allowed) in violations)
            {
                if (!allowed)
                {
                    unallowed = (invariantId, allowed);
                    break;
                }
            }
        }

        (RowGrade grade, string reason) = GradeClauses(required, anyNotHeld, anyUnverified, anyPending, unallowed);
        if (checkErrors is { Count: > 0 } && grade is RowGrade.Pass or RowGrade.NeedsHuman or RowGrade.Unverified)
        {
            InvariantCheckError first = checkErrors[0];
            return (RowGrade.Unverified, "invariant " + first.InvariantId + " unchecked: " + first.Error);
        }

        return (grade, reason);
    }

    private static (RowGrade Grade, string Reason) GradeClauses(int required, bool anyNotHeld, bool anyUnverified, bool anyPending, (string InvariantId, bool Allowed)? unallowed)
    {
        if (anyNotHeld)
        {
            return (RowGrade.Fail, "a required clause is not held");
        }

        if (unallowed is { } violation)
        {
            return (RowGrade.Fail, "invariant " + violation.InvariantId + " violated");
        }

        if (required == 0)
        {
            return (RowGrade.Unverified, "no required clause");
        }

        if (anyUnverified)
        {
            return (RowGrade.Unverified, "a required clause could not be verified");
        }

        if (anyPending)
        {
            return (RowGrade.NeedsHuman, "a required human clause is pending attestation");
        }

        return (RowGrade.Pass, "every required clause is held");
    }
}

/// <summary>
/// Computes the overall run result from the row grades and the failures (C8). Until L25 every failure is new.
/// </summary>
public static class RunOutcome
{
    /// <summary>
    /// Computes <see cref="RunOverall"/> in precedence order: fail, incomplete, pass_with_known, pass.
    /// </summary>
    /// <param name="grades">The grades of the rows that were recorded (filtered-out rows are not included).</param>
    /// <param name="failures">One entry per failed row or unallowed violation, with whether it is linked to an open known issue.</param>
    public static RunOverall Compute(IReadOnlyList<RowGrade> grades, IReadOnlyList<(string FailureKey, bool IsKnown)> failures)
    {
        ArgumentNullException.ThrowIfNull(grades);
        ArgumentNullException.ThrowIfNull(failures);

        if (failures.Any(f => !f.IsKnown))
        {
            return RunOverall.Fail;
        }

        if (grades.Count == 0 || grades.Any(g => g is RowGrade.NeedsHuman or RowGrade.Skipped))
        {
            return RunOverall.Incomplete;
        }

        return failures.Count > 0 ? RunOverall.PassWithKnown : RunOverall.Pass;
    }
}
