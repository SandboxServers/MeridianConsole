using System.Globalization;
using System.Text;

namespace Dhadgar.Lab.Uat.Bundle;

/// <summary>
/// One clause as the ledger shows it: its id, its pointer and its state.
/// </summary>
/// <param name="Id">The clause id.</param>
/// <param name="Locator">The JSON pointer or the source name when the clause has no pointer.</param>
/// <param name="State">The clause state after evaluation.</param>
/// <param name="Error">The error text, or null.</param>
public sealed record LedgerClause(string Id, string Locator, ClauseState State, string? Error, string Source = "", bool Required = true);

/// <summary>
/// One row as the ledger shows it.
/// </summary>
/// <param name="Section">The section id.</param>
/// <param name="Row">The row id.</param>
/// <param name="Title">The row title from the spec.</param>
/// <param name="Expected">The row's expected text from the spec.</param>
/// <param name="Grade">The row grade.</param>
/// <param name="At">When the row was recorded.</param>
/// <param name="Clauses">The clauses that ran, in spec order.</param>
public sealed record LedgerRow(string Section, string Row, string Title, string Expected, RowGrade Grade, DateTimeOffset At, IReadOnlyList<LedgerClause> Clauses);

/// <summary>
/// Renders <c>ledger.md</c> in the C9 shape: a summary table, then one section per row. Pipes in the table have spaces around them.
/// </summary>
public static class LedgerRenderer
{
    /// <summary>
    /// Renders the ledger. Lines end with <c>\n</c>.
    /// </summary>
    /// <param name="runId">The six-character run id.</param>
    /// <param name="system">The system line, for example <c>meridian-lab 1.0.0+a1b2c3d4 on Windows 10.0.26300</c>.</param>
    /// <param name="build">The build line, for example <c>a1b2c3d4 (commit 0123456789ab, clean)</c>.</param>
    /// <param name="rows">The recorded rows, in file order.</param>
    public static string Render(string runId, string system, string build, IReadOnlyList<LedgerRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        text.Append("# UAT run ").Append(runId).Append('\n').Append('\n');
        text.Append("| Section | Row | Result | Time (UTC) | Build |\n");
        text.Append("| --- | --- | --- | --- | --- |\n");
        foreach (LedgerRow row in rows)
        {
            text.Append("| ").Append(row.Section).Append(" | ").Append(row.Row).Append(" | ")
                .Append(ResultText(row.Grade)).Append(" | ").Append(Time(row.At)).Append(" | ").Append(build.Split(' ')[0]).Append(" |\n");
        }

        text.Append('\n').Append("## Recording results\n");
        foreach (LedgerRow row in rows)
        {
            text.Append('\n');
            text.Append("### ").Append(row.Section).Append('/').Append(row.Row).Append(": ").Append(row.Title).Append('\n');
            text.Append('\n');
            text.Append("- System: ").Append(system).Append('\n');
            text.Append("- Step id: ").Append(row.Section).Append('/').Append(row.Row).Append('\n');
            text.Append("- Result: ").Append(ResultText(row.Grade)).Append('\n');
            text.Append("- Time: ").Append(Time(row.At)).Append('\n');
            text.Append("- Build: ").Append(build).Append('\n');
            text.Append("- Saw: ").Append(Saw(row.Clauses)).Append('\n');
            text.Append("- Expected: ").Append(row.Expected).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// The result word for a grade, as the ledger prints it.
    /// </summary>
    /// <param name="grade">The row grade.</param>
    public static string ResultText(RowGrade grade) => grade switch
    {
        RowGrade.Pass => "PASS",
        RowGrade.Fail => "FAIL",
        RowGrade.Blocked => "BLOCKED",
        RowGrade.Skipped => "SKIPPED",
        RowGrade.NeedsHuman => "NEEDS_HUMAN",
        RowGrade.Unverified => "UNVERIFIED",
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    private static string Time(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Saw(IReadOnlyList<LedgerClause> clauses)
    {
        if (clauses.Count == 0)
        {
            return "none";
        }

        return string.Join("; ", clauses.Select(clause =>
            clause.Id + ": " + clause.Locator + " ("+ StateText(clause.State) + ")" +
            (clause.Error is { Length: > 0 } error ? " error: " + error : string.Empty)));
    }

    private static string StateText(ClauseState state) => state switch
    {
        ClauseState.Held => "held",
        ClauseState.NotHeld => "not_held",
        ClauseState.Unverified => "unverified",
        ClauseState.Pending => "pending",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
