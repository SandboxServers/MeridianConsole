using Dhadgar.Lab.Uat;

namespace Dhadgar.Lab.Results;

/// <summary>
/// One graded row of a run. Run-start and run-end violations arrive as the synthetic rows <c>invariants/&lt;id&gt;</c>
/// (<c>UatRunner.SyntheticRows</c>).
/// </summary>
public sealed record GradedRow(string Section, string RowId, RowGrade Grade);

/// <summary>
/// The overall result of a run and its failures (C12). <c>new_failures</c> and <c>known_failures</c> hold failure keys of the
/// form <c>section/row</c>.
/// </summary>
public sealed record RunSummary(RunOverall Overall, IReadOnlyList<string> NewFailures, IReadOnlyList<string> KnownFailures)
{
    /// <summary>The C8 JSON value of <see cref="Overall"/>: pass, pass_with_known, incomplete or fail.</summary>
    public string OverallText => Overall switch
    {
        RunOverall.Pass => "pass",
        RunOverall.PassWithKnown => "pass_with_known",
        RunOverall.Incomplete => "incomplete",
        _ => "fail",
    };

    /// <summary>
    /// The one rule for a run's overall and failures (A1). A failure is FAIL, BLOCKED or UNVERIFIED for a row. It is known only
    /// when its match is <c>known</c>, which needs one open issue that matches every signature. The overall is C8's
    /// <see cref="RunOutcome.Compute"/>. Pure: no IO, no time.
    /// </summary>
    /// <param name="rows">Every graded row of the run, and any standalone unallowed violation.</param>
    /// <param name="matches">The known-issue verdict by failure key (<c>section/row</c>).</param>
    public static RunSummary Compute(IReadOnlyList<GradedRow> rows, IReadOnlyDictionary<string, KnownMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(matches);

        var failures = new List<(string FailureKey, bool IsKnown)>();
        var newFailures = new List<string>();
        var knownFailures = new List<string>();
        foreach (GradedRow row in rows.Where(r => r.Grade is RowGrade.Fail or RowGrade.Blocked or RowGrade.Unverified))
        {
            string key = row.Section + "/" + row.RowId;
            bool known = matches.TryGetValue(key, out KnownMatch? match) && match.State == "known";
            failures.Add((key, known));
            (known ? knownFailures : newFailures).Add(key);
        }

        RunOverall overall = RunOutcome.Compute(rows.Select(r => r.Grade).ToList(), failures);
        return new RunSummary(overall, newFailures, knownFailures);
    }
}
