using Dhadgar.Lab.Results;
using Dhadgar.Lab.Uat;

namespace Dhadgar.Lab.Tests.Results;

/// <summary>
/// The one run summary rule (A1, C8/C12): a standalone unallowed violation with no issue is a failure, and a known failure
/// next to a skipped row is incomplete.
/// </summary>
public sealed class OverallTests
{
    private static readonly IReadOnlyDictionary<string, KnownMatch> NoMatches = new Dictionary<string, KnownMatch>();

    [Fact]
    public void Overall_AllRowsPass_IsPass()
    {
        RunSummary summary = RunSummary.Compute(
            [new GradedRow("nodes", "enroll", RowGrade.Pass), new GradedRow("nodes", "heartbeat", RowGrade.Pass)],
            NoMatches);

        Assert.Equal(RunOverall.Pass, summary.Overall);
        Assert.Equal("pass", summary.OverallText);
        Assert.Empty(summary.NewFailures);
    }

    [Fact]
    public void Overall_KnownFailureOnly_IsPassWithKnown()
    {
        var matches = new Dictionary<string, KnownMatch> { ["nodes/enroll"] = new("KI-F67", "known", null) };

        RunSummary summary = RunSummary.Compute(
            [new GradedRow("nodes", "enroll", RowGrade.Fail), new GradedRow("nodes", "heartbeat", RowGrade.Pass)],
            matches);

        Assert.Equal("pass_with_known", summary.OverallText);
        Assert.Equal(["nodes/enroll"], summary.KnownFailures);
        Assert.Empty(summary.NewFailures);
    }

    [Fact]
    public void Overall_KnownFailuresPlusSkippedRow_IsIncomplete()
    {
        var matches = new Dictionary<string, KnownMatch> { ["nodes/enroll"] = new("KI-F67", "known", null) };

        RunSummary summary = RunSummary.Compute(
            [new GradedRow("nodes", "enroll", RowGrade.Fail), new GradedRow("nodes", "heartbeat", RowGrade.Skipped)],
            matches);

        Assert.Equal("incomplete", summary.OverallText);
    }

    [Fact]
    public void Overall_NewFailure_IsFail()
    {
        RunSummary summary = RunSummary.Compute(
            [new GradedRow("nodes", "enroll", RowGrade.Fail), new GradedRow("nodes", "heartbeat", RowGrade.Pass)],
            NoMatches);

        Assert.Equal("fail", summary.OverallText);
        Assert.Equal(["nodes/enroll"], summary.NewFailures);
    }

    [Fact]
    public void Overall_UnlinkedViolation_IsFail()
    {
        // A standalone unallowed violation is passed as a failed row of section "violation" (RunSummary).
        RunSummary summary = RunSummary.Compute(
            [new GradedRow("nodes", "enroll", RowGrade.Pass), new GradedRow("violation", "I1/0123456789abcdef", RowGrade.Fail)],
            NoMatches);

        Assert.Equal("fail", summary.OverallText);
        Assert.Contains("violation/I1/0123456789abcdef", summary.NewFailures);
    }

    [Fact]
    public void Overall_UnlinkedViolationNextToKnownFailure_IsStillFail()
    {
        var matches = new Dictionary<string, KnownMatch> { ["nodes/enroll"] = new("KI-F67", "known", null) };

        RunSummary summary = RunSummary.Compute(
            [new GradedRow("nodes", "enroll", RowGrade.Fail), new GradedRow("violation", "I1/0123456789abcdef", RowGrade.Fail)],
            matches);

        Assert.Equal("fail", summary.OverallText);
    }
}
