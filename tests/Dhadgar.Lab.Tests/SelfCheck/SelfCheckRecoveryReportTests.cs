using Dhadgar.Lab.SelfCheck;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The printed recovery verdict (security N3): a note is printed and never fails the leg; a problem fails it. Pure, no Docker.
/// </summary>
public sealed class SelfCheckRecoveryReportTests
{
    [Fact]
    public void NoteOnly_PrintsTheNoteAndPasses()
    {
        const string note = "I5 queues not checked after the restart: stack not up (no lab credentials)";
        using var output = new StringWriter();

        int exit = SelfCheckProgram.ReportRecovery(new RecoveryVerdict([], [note]), output);

        Assert.Equal(0, exit);
        string text = output.ToString();
        Assert.Contains("selfcheck recovery: note: " + note, text, StringComparison.Ordinal);
        Assert.Contains("selfcheck recovery: passed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Problem_PrintsTheNoteAndFails()
    {
        const string note = "I5 proxies not checked after the restart: stack not up (no lab credentials)";
        using var output = new StringWriter();

        int exit = SelfCheckProgram.ReportRecovery(new RecoveryVerdict(["p"], [note]), output);

        Assert.Equal(1, exit);
        string text = output.ToString();
        Assert.Contains("selfcheck recovery: note: " + note, text, StringComparison.Ordinal);
        Assert.Contains("selfcheck recovery: p", text, StringComparison.Ordinal);
        Assert.DoesNotContain("selfcheck recovery: passed", text, StringComparison.Ordinal);
    }
}
