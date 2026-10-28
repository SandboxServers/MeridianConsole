using Dhadgar.Lab.Uat;

namespace Dhadgar.Lab.Tests.Uat;

public sealed class RowGraderTests
{
    private static (RowGrade Grade, string Reason) Grade(params (bool Required, ClauseState State)[] clauses) =>
        RowGrader.Grade(ran: true, skipReason: null, stepError: null, clauses: clauses);

    [Fact]
    public void Grade_DidNotRun_IsSkippedWithReason()
    {
        var result = RowGrader.Grade(ran: false, skipReason: "capability windows_containers missing", stepError: null, clauses: []);

        Assert.Equal(RowGrade.Skipped, result.Grade);
        Assert.Equal("capability windows_containers missing", result.Reason);
    }

    [Fact]
    public void Grade_DidNotRunWithoutReason_IsSkipped()
    {
        var result = RowGrader.Grade(ran: false, skipReason: null, stepError: null, clauses: []);

        Assert.Equal(RowGrade.Skipped, result.Grade);
    }

    [Fact]
    public void Grade_StepError_IsBlockedEvenWithNotHeldClause()
    {
        var result = RowGrader.Grade(ran: true, skipReason: null, stepError: "[Lab.LeaseLost] lease lost", clauses: [(true, ClauseState.NotHeld)]);

        Assert.Equal(RowGrade.Blocked, result.Grade);
        Assert.Equal("[Lab.LeaseLost] lease lost", result.Reason);
    }

    [Fact]
    public void Grade_RequiredNotHeld_IsFail()
    {
        Assert.Equal(RowGrade.Fail, Grade((true, ClauseState.NotHeld)).Grade);
    }

    [Fact]
    public void Grade_NotHeldBeatsUnverified_IsFail()
    {
        Assert.Equal(RowGrade.Fail, Grade((true, ClauseState.Unverified), (true, ClauseState.NotHeld)).Grade);
    }

    [Fact]
    public void Grade_NotHeldBeatsPending_IsFail()
    {
        Assert.Equal(RowGrade.Fail, Grade((true, ClauseState.Pending), (true, ClauseState.NotHeld)).Grade);
    }

    [Fact]
    public void Grade_NoRequiredClauses_IsUnverified()
    {
        var result = Grade((false, ClauseState.Held), (false, ClauseState.NotHeld));

        Assert.Equal(RowGrade.Unverified, result.Grade);
        Assert.Equal("no required clause", result.Reason);
    }

    [Fact]
    public void Grade_NoClausesAtAll_IsUnverified()
    {
        Assert.Equal(RowGrade.Unverified, Grade().Grade);
    }

    [Fact]
    public void Grade_RequiredUnverified_IsUnverified()
    {
        Assert.Equal(RowGrade.Unverified, Grade((true, ClauseState.Held), (true, ClauseState.Unverified)).Grade);
    }

    [Fact]
    public void Grade_UnverifiedBeatsPending_IsUnverified()
    {
        Assert.Equal(RowGrade.Unverified, Grade((true, ClauseState.Pending), (true, ClauseState.Unverified)).Grade);
    }

    [Fact]
    public void Grade_PendingHuman_IsNeedsHuman()
    {
        Assert.Equal(RowGrade.NeedsHuman, Grade((true, ClauseState.Held), (true, ClauseState.Pending)).Grade);
    }

    [Fact]
    public void Grade_AllRequiredHeld_IsPass()
    {
        Assert.Equal(RowGrade.Pass, Grade((true, ClauseState.Held), (true, ClauseState.Held)).Grade);
    }

    [Fact]
    public void Grade_NonRequiredNotHeld_DoesNotChangePass()
    {
        Assert.Equal(RowGrade.Pass, Grade((true, ClauseState.Held), (false, ClauseState.NotHeld)).Grade);
    }

    [Fact]
    public void Grade_NonRequiredUnverified_DoesNotChangePass()
    {
        Assert.Equal(RowGrade.Pass, Grade((true, ClauseState.Held), (false, ClauseState.Unverified)).Grade);
    }

    [Fact]
    public void Grade_NonRequiredPending_DoesNotChangePass()
    {
        Assert.Equal(RowGrade.Pass, Grade((true, ClauseState.Held), (false, ClauseState.Pending)).Grade);
    }

    [Fact]
    public void Grade_NonRequiredNotHeldWithRequiredPending_IsNeedsHuman()
    {
        Assert.Equal(RowGrade.NeedsHuman, Grade((true, ClauseState.Pending), (false, ClauseState.NotHeld)).Grade);
    }
}
