using Dhadgar.Lab.Uat;

namespace Dhadgar.Lab.Tests.Uat;

public sealed class RunOutcomeTests
{
    private static readonly (string FailureKey, bool IsKnown)[] NoFailures = [];

    [Fact]
    public void Overall_AllPass_IsPass()
    {
        RunOverall overall = RunOutcome.Compute([RowGrade.Pass, RowGrade.Pass], NoFailures);

        Assert.Equal(RunOverall.Pass, overall);
    }

    [Fact]
    public void Overall_AllSkipped_IsIncomplete()
    {
        RunOverall overall = RunOutcome.Compute([RowGrade.Skipped, RowGrade.Skipped], NoFailures);

        Assert.Equal(RunOverall.Incomplete, overall);
    }

    [Fact]
    public void Overall_NoRows_IsIncomplete()
    {
        RunOverall overall = RunOutcome.Compute([], NoFailures);

        Assert.Equal(RunOverall.Incomplete, overall);
    }

    [Fact]
    public void Overall_NeedsHuman_IsIncomplete()
    {
        RunOverall overall = RunOutcome.Compute([RowGrade.Pass, RowGrade.NeedsHuman], NoFailures);

        Assert.Equal(RunOverall.Incomplete, overall);
    }

    [Fact]
    public void Overall_FailAndSkipped_IsFail()
    {
        RunOverall overall = RunOutcome.Compute(
            [RowGrade.Fail, RowGrade.Skipped],
            [("nodes/enroll-direct", IsKnown: false)]);

        Assert.Equal(RunOverall.Fail, overall);
    }

    [Fact]
    public void Overall_OnlyKnownAndSkipped_IsIncomplete()
    {
        RunOverall overall = RunOutcome.Compute(
            [RowGrade.Fail, RowGrade.Skipped],
            [("nodes/enroll-direct", IsKnown: true)]);

        Assert.Equal(RunOverall.Incomplete, overall);
    }

    [Fact]
    public void Overall_OnlyKnown_IsPassWithKnown()
    {
        RunOverall overall = RunOutcome.Compute(
            [RowGrade.Pass, RowGrade.Fail],
            [("nodes/enroll-direct", IsKnown: true)]);

        Assert.Equal(RunOverall.PassWithKnown, overall);
    }

    [Fact]
    public void Overall_NewFailure_IsFail()
    {
        RunOverall overall = RunOutcome.Compute([RowGrade.Pass, RowGrade.Blocked], [("x/y", IsKnown: false)]);

        Assert.Equal(RunOverall.Fail, overall);
    }
}
