namespace Dhadgar.Lab.SelfCheck;

/// <summary>One line of <c>selfcheck.json</c>: a lab mutant, its named test, its verdict and a short reason.</summary>
/// <param name="Id">The mutant id.</param>
/// <param name="Test">The fully qualified test the mutant names.</param>
/// <param name="Verdict">One of <see cref="SelfCheckJudge.Caught"/>, <see cref="SelfCheckJudge.Missed"/> or <see cref="SelfCheckJudge.Invalid"/>.</param>
/// <param name="Detail">A short, fixed reason. Never a log line or a secret.</param>
public sealed record SelfCheckRow(string Id, string Test, string Verdict, string Detail);

/// <summary>
/// The verdict of one lab mutant (L55, C19). A mutant is <c>caught</c> only when its named test ran and failed in the TRX.
/// A build failure, zero tests run or a skipped test is <c>invalid</c>, never <c>caught</c>. A baseline miss (a named test
/// that does not pass on the unmodified copy) makes every verdict <c>invalid</c>.
/// </summary>
public static class SelfCheckJudge
{
    /// <summary>The detector caught the mutant: its test ran and failed.</summary>
    public const string Caught = "caught";

    /// <summary>The detector ran and passed: the lab cannot see this defect.</summary>
    public const string Missed = "missed";

    /// <summary>The self-check could not judge: build, run or baseline problem.</summary>
    public const string Invalid = "invalid";

    private const string PassedOutcome = "Passed";
    private const string FailedOutcome = "Failed";

    /// <summary>
    /// Judges the baseline for one named test: null when every row for that test passed (and at least one ran), else the reason.
    /// </summary>
    /// <param name="test">The fully qualified test name.</param>
    /// <param name="baselineRows">The TRX rows of the unmodified copy.</param>
    public static string? BaselineProblem(string test, IReadOnlyList<TrxTestResult> baselineRows)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(baselineRows);

        List<TrxTestResult> named = Named(test, baselineRows);
        if (named.Count == 0)
        {
            return "baseline: the named test did not run on the unmodified copy";
        }

        return named.All(row => row.Outcome == PassedOutcome)
            ? null
            : "baseline: the named test does not pass on the unmodified copy";
    }

    /// <summary>
    /// Judges one mutant from its build result and the TRX rows of its test run.
    /// </summary>
    /// <param name="id">The mutant id.</param>
    /// <param name="test">The fully qualified test the mutant names.</param>
    /// <param name="built">True when the patched copy built.</param>
    /// <param name="rows">The TRX rows of the run (empty when the test did not run).</param>
    public static SelfCheckRow Judge(string id, string test, bool built, IReadOnlyList<TrxTestResult> rows)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(rows);

        if (!built)
        {
            return new SelfCheckRow(id, test, Invalid, "build failed; a build failure is never caught");
        }

        List<TrxTestResult> named = Named(test, rows);
        if (named.Count == 0)
        {
            return new SelfCheckRow(id, test, Invalid, "zero tests run: the named test did not run");
        }

        if (named.Any(row => row.Outcome is not (PassedOutcome or FailedOutcome)))
        {
            return new SelfCheckRow(id, test, Invalid, "the named test was skipped or not executed");
        }

        return named.Any(row => row.Outcome == FailedOutcome)
            ? new SelfCheckRow(id, test, Caught, "the named test ran and failed")
            : new SelfCheckRow(id, test, Missed, "the named test ran and passed");
    }

    /// <summary>
    /// Every mutant <c>invalid</c> with one reason: used when the baseline fails, so no mutant can be <c>caught</c> against it.
    /// </summary>
    /// <param name="mutants">The mutant id and its named test.</param>
    /// <param name="reason">The fixed reason written to each row.</param>
    public static IReadOnlyList<SelfCheckRow> AllInvalid(IEnumerable<(string Id, string Test)> mutants, string reason)
    {
        ArgumentNullException.ThrowIfNull(mutants);
        ArgumentNullException.ThrowIfNull(reason);

        return mutants.Select(m => new SelfCheckRow(m.Id, m.Test, Invalid, reason)).ToList();
    }

    /// <summary>
    /// The rows that belong to <paramref name="test"/>. A TRX name is the fully qualified name, the bare method name, or a
    /// theory name with its arguments in parentheses.
    /// </summary>
    private static List<TrxTestResult> Named(string test, IReadOnlyList<TrxTestResult> rows) =>
        rows.Where(row => NamesTest(row.TestName, test)).ToList();

    private static bool NamesTest(string trxName, string test)
    {
        if (string.Equals(trxName, test, StringComparison.Ordinal) || trxName.StartsWith(test + "(", StringComparison.Ordinal))
        {
            return true;
        }

        return test.EndsWith("." + trxName, StringComparison.Ordinal);
    }
}
