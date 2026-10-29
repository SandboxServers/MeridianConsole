using Dhadgar.Lab.SelfCheck;

namespace Dhadgar.Lab.Tests.SelfCheck;

/// <summary>
/// The self-check verdicts (L55): a mutant is caught only when its named test ran and failed. Pure, no processes.
/// </summary>
public sealed class SelfCheckJudgeTests
{
    private const string Test = "Dhadgar.Lab.Tests.Faults.FaultBookTests.LeaseEnd_HealsAllFaults";
    private const string Id = "LM3-lease-end-no-heal";

    [Fact]
    public void BuildFailed_IsInvalidNotCaught()
    {
        // Even a TRX that says Failed cannot make a build failure "caught".
        var rows = new[] { new TrxTestResult(Test, "Failed") };

        SelfCheckRow row = SelfCheckJudge.Judge(Id, Test, built: false, rows);

        Assert.Equal(SelfCheckJudge.Invalid, row.Verdict);
    }

    [Fact]
    public void ZeroTestsRun_IsInvalid()
    {
        SelfCheckRow row = SelfCheckJudge.Judge(Id, Test, built: true, []);

        Assert.Equal(SelfCheckJudge.Invalid, row.Verdict);
        Assert.Contains("zero tests run", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherTestsRan_TheNamedTestDidNot_IsInvalid()
    {
        var rows = new[] { new TrxTestResult("Dhadgar.Lab.Tests.Faults.FaultBookTests.Other", "Failed") };

        SelfCheckRow row = SelfCheckJudge.Judge(Id, Test, built: true, rows);

        Assert.Equal(SelfCheckJudge.Invalid, row.Verdict);
    }

    [Fact]
    public void NamedTestSkipped_IsInvalid_NotMissed()
    {
        var rows = new[] { new TrxTestResult(Test, "NotExecuted") };

        SelfCheckRow row = SelfCheckJudge.Judge(Id, Test, built: true, rows);

        Assert.Equal(SelfCheckJudge.Invalid, row.Verdict);
    }

    [Fact]
    public void NamedTestFailed_IsCaught()
    {
        var rows = new[] { new TrxTestResult(Test, "Failed") };

        SelfCheckRow row = SelfCheckJudge.Judge(Id, Test, built: true, rows);

        Assert.Equal(SelfCheckJudge.Caught, row.Verdict);
    }

    [Fact]
    public void NamedTestPassed_IsMissed()
    {
        var rows = new[] { new TrxTestResult(Test, "Passed") };

        SelfCheckRow row = SelfCheckJudge.Judge(Id, Test, built: true, rows);

        Assert.Equal(SelfCheckJudge.Missed, row.Verdict);
    }

    [Fact]
    public void TrxNameWithBareMethodName_StillMatchesTheTest()
    {
        var rows = new[] { new TrxTestResult("LeaseEnd_HealsAllFaults", "Failed") };

        SelfCheckRow row = SelfCheckJudge.Judge(Id, Test, built: true, rows);

        Assert.Equal(SelfCheckJudge.Caught, row.Verdict);
    }

    [Fact]
    public void BaselineFails_AllInvalid()
    {
        var baseline = new[] { new TrxTestResult(Test, "Failed") };

        string? problem = SelfCheckJudge.BaselineProblem(Test, baseline);
        IReadOnlyList<SelfCheckRow> rows = SelfCheckJudge.AllInvalid([(Id, Test), ("LM4-tripwire-name-only", "x.Y.Z")], problem!);

        Assert.NotNull(problem);
        Assert.All(rows, row => Assert.Equal(SelfCheckJudge.Invalid, row.Verdict));
    }

    [Fact]
    public void BaselinePasses_NoProblem()
    {
        var baseline = new[] { new TrxTestResult(Test, "Passed") };

        Assert.Null(SelfCheckJudge.BaselineProblem(Test, baseline));
    }

    [Fact]
    public void BaselineDidNotRun_IsAProblem()
    {
        Assert.NotNull(SelfCheckJudge.BaselineProblem(Test, []));
    }
}
