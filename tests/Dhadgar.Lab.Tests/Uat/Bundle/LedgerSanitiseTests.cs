using Dhadgar.Lab.Uat;
using Dhadgar.Lab.Uat.Bundle;
using Dhadgar.Lab.Uat.Eval;
using Xunit;

namespace Dhadgar.Lab.Tests.Uat.Bundle;

public sealed class LedgerSanitiseTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Render_ControlCharsInTitleAndError_StayOnOneLine()
    {
        var clauses = new[] { new LedgerClause("c1", "/ok", ClauseState.NotHeld, "boom\n| x | PASS |") };
        var rows = new[] { new LedgerRow("plan-check", "key", "Title\n| x | PASS |", "expected", RowGrade.Fail, At, clauses) };

        string ledger = LedgerRenderer.Render("abc123", "linux", "a1b2c3d4 (commit 0123456789ab, clean)", rows);

        Assert.DoesNotContain("\n| x | PASS |", ledger, StringComparison.Ordinal);
        Assert.Contains("### plan-check/key: Title | x | PASS |", ledger, StringComparison.Ordinal);
        Assert.Contains("error: boom | x | PASS |", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PipeInTableCell_IsEscaped()
    {
        var rows = new[] { new LedgerRow("plan-check", "r|x", "Title", "expected", RowGrade.Pass, At, Array.Empty<LedgerClause>()) };

        string ledger = LedgerRenderer.Render("abc123", "linux", "a1b2c3d4 (commit 0123456789ab, clean)", rows);

        Assert.Contains("| plan-check | r\\|x | PASS |", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_AttestationNoteWithControlChars_StaysOnOneLine()
    {
        var attestations = new[] { new LedgerAttestation("c1", "held", "fine\r\n| x | PASS |", At, "owner-a", "mcp") };
        var rows = new[] { new LedgerRow("plan-check", "key", "Title", "expected", RowGrade.Pass, At, Array.Empty<LedgerClause>(), attestations) };

        string ledger = LedgerRenderer.Render("abc123", "linux", "a1b2c3d4 (commit 0123456789ab, clean)", rows);

        Assert.DoesNotContain("\n| x | PASS |", ledger, StringComparison.Ordinal);
        Assert.Contains("- Attested: c1 held by owner-a via mcp at 2026-01-01T00:00:00Z; note: fine | x | PASS |", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_LineAndParagraphSeparatorsAndBidiOverride_BecomeSpaces()
    {
        var rows = new[] { new LedgerRow("plan-check", "key", "a\u2028b\u202Ec\u2029d", "expected", RowGrade.Pass, At, Array.Empty<LedgerClause>()) };

        string ledger = LedgerRenderer.Render("abc123", "linux", "a1b2c3d4 (commit 0123456789ab, clean)", rows);

        Assert.DoesNotContain("\u2028", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2029", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", ledger, StringComparison.Ordinal);
        Assert.Contains("### plan-check/key: a b c d", ledger, StringComparison.Ordinal);
    }
}
