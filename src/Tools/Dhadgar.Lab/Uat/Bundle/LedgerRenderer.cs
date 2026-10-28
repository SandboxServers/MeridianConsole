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
/// <param name="Attestations">The human attestations recorded on the row, in order, or null when there are none.</param>
public sealed record LedgerRow(string Section, string Row, string Title, string Expected, RowGrade Grade, DateTimeOffset At, IReadOnlyList<LedgerClause> Clauses, IReadOnlyList<LedgerAttestation>? Attestations = null);

/// <summary>
/// One human verdict recorded on a row, as the ledger shows it.
/// </summary>
/// <param name="ClauseId">The clause the verdict answers.</param>
/// <param name="Verdict">The verdict word: <c>held</c> or <c>not_held</c>.</param>
/// <param name="Note">The note, or an empty string.</param>
/// <param name="At">When the verdict was recorded.</param>
/// <param name="LeaseOwner">The owner of the lease the attest call carried.</param>
/// <param name="Caller">The caller that made the attest call.</param>
public sealed record LedgerAttestation(string ClauseId, string Verdict, string Note, DateTimeOffset At, string LeaseOwner, string Caller);

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
            text.Append("| ").Append(Cell(row.Section)).Append(" | ").Append(Cell(row.Row)).Append(" | ")
                .Append(ResultText(row.Grade)).Append(" | ").Append(Time(row.At)).Append(" | ").Append(Cell(build.Split(' ')[0])).Append(" |\n");
        }

        text.Append('\n').Append("## Recording results\n");
        foreach (LedgerRow row in rows)
        {
            text.Append('\n');
            text.Append("### ").Append(Line(row.Section)).Append('/').Append(Line(row.Row)).Append(": ").Append(Line(row.Title)).Append('\n');
            text.Append('\n');
            text.Append("- System: ").Append(Line(system)).Append('\n');
            text.Append("- Step id: ").Append(Line(row.Section)).Append('/').Append(Line(row.Row)).Append('\n');
            text.Append("- Result: ").Append(ResultText(row.Grade)).Append('\n');
            text.Append("- Time: ").Append(Time(row.At)).Append('\n');
            text.Append("- Build: ").Append(Line(build)).Append('\n');
            text.Append("- Saw: ").Append(Saw(row.Clauses)).Append('\n');
            text.Append("- Expected: ").Append(Line(row.Expected)).Append('\n');
            foreach (LedgerAttestation attestation in row.Attestations ?? [])
            {
                text.Append("- Attested: ").Append(Line(attestation.ClauseId)).Append(' ').Append(Line(attestation.Verdict))
                    .Append(" by ").Append(Line(attestation.LeaseOwner)).Append(" via ").Append(Line(attestation.Caller))
                    .Append(" at ").Append(Time(attestation.At));
                if (attestation.Note.Length > 0)
                {
                    text.Append("; note: ").Append(Line(attestation.Note));
                }

                text.Append('\n');
            }
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
            Line(clause.Id) + ": " + Line(clause.Locator) + " ("+ StateText(clause.State) + ")" +
            (clause.Error is { Length: > 0 } error ? " error: " + Line(error) : string.Empty)));
    }

    /// <summary>
    /// Makes free text safe for one ledger line: CR, LF and every other control, format, separator, private-use or
    /// unassigned code point become a space.
    /// </summary>
    /// <param name="text">The free text.</param>
    internal static string Line(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (Rune rune in text.Replace("\r\n", " ", StringComparison.Ordinal).EnumerateRunes())
        {
            builder.Append(IsUnsafeRune(rune) ? " " : rune.ToString());
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when the text holds a code point that can hide, reorder or split content on a ledger line: a control,
    /// format (bidi overrides and tag characters), line or paragraph separator, private-use or unassigned code point.
    /// </summary>
    /// <param name="text">The free text.</param>
    internal static bool IsUnsafeText(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (IsUnsafeRune(rune))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsafeRune(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
        or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
        or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;

    /// <summary>
    /// Makes free text safe for one markdown table cell: a line (see <see cref="Line"/>) with each pipe escaped.
    /// </summary>
    /// <param name="text">The free text.</param>
    internal static string Cell(string text) => Line(text).Replace("|", "\\|", StringComparison.Ordinal);

    private static string StateText(ClauseState state) => state switch
    {
        ClauseState.Held => "held",
        ClauseState.NotHeld => "not_held",
        ClauseState.Unverified => "unverified",
        ClauseState.Pending => "pending",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
