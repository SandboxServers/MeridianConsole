using Dhadgar.Lab.SelfCheck;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The recovery diagnostics (D3, R3-N7, D-ML102): a tool's error text is bounded, and a child that exits before it answers names its
/// exit code and its first <c>[Lab.</c> line. Pure, no processes, no Docker.
/// </summary>
public sealed class SelfCheckRecoveryDiagnosticsTests
{
    [Fact]
    public void ToolFailure_KeepsTheText_Bounded()
    {
        string prefix = "[Lab.Tool] uat_run failed: ";

        string shortText = SelfCheckRecovery.ToolFailure("uat_run", "[Lab.Busy] try later");
        string longText = SelfCheckRecovery.ToolFailure("uat_run", new string('x', 500));

        Assert.Equal(prefix + "[Lab.Busy] try later", shortText);
        Assert.Equal(prefix.Length + 300, longText.Length);
        Assert.StartsWith(prefix + new string('x', 300), longText, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitedEarly_NamesCodeAndLine()
    {
        string text = SelfCheckRecovery.ExitedEarly(3, "[Lab.Port] the daemon port is busy");

        Assert.Equal(
            "[Lab.ProcessFailed] the lab daemon exited with code 3 before answering /status: [Lab.Port] the daemon port is busy",
            text);
    }

    [Fact]
    public void ExitedEarly_WithoutLine_SaysSo()
    {
        string text = SelfCheckRecovery.ExitedEarly(1, null);

        Assert.Equal("[Lab.ProcessFailed] the lab daemon exited with code 1 before answering /status: no [Lab.] line", text);
    }
}
