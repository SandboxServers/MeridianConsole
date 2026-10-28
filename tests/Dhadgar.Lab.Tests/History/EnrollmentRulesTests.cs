using Dhadgar.Lab.History.Rules;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// The enrollment rules E1-E6 (C19) on hand-written histories and final states.
/// </summary>
public sealed class EnrollmentRulesTests
{
    [Fact]
    public void Rules_E1_TwoDistinctNodes_Violation()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n1", ("n1", "lab-r1-c1-1", 1), ("n2", "lab-r1-c1-2", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "n1"),
            EnrollmentFixtures.Invoke(3, "op-2", "client:2", "lab-r1-c1-2"), EnrollmentFixtures.Complete(4, "op-2", "client:2", OpOutcome.Ok, 201, "n2"));

        var violations = EnrollmentRules.Evaluate(history);

        Assert.Contains(violations, v => v.Rule == "E1" && v.Required);
    }

    [Fact]
    public void Rules_E2_TwoActiveCerts_Violation()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n1", ("n1", "lab-r1-c1-1", 2)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "n1"));

        var violation = Assert.Single(EnrollmentRules.Evaluate(history));

        Assert.Equal("E2", violation.Rule);
        Assert.True(violation.Required);
        Assert.Equal("op-1", Assert.Single(violation.OpIds));
    }

    [Fact]
    public void Rules_E3_FailedOpMadeNode_Violation()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(false, null, ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Fail, 400));

        var violations = EnrollmentRules.Evaluate(history);

        var e3 = Assert.Single(violations, v => v.Rule == "E3");
        Assert.True(e3.Required);
        Assert.Equal("op-1", Assert.Single(e3.OpIds));
    }

    [Fact]
    public void Rules_E5_RequiredOnlyWhenIdempotent()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n1", ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"),
            EnrollmentFixtures.Invoke(3, "op-2", "client:1", "lab-r1-c1-2"), EnrollmentFixtures.Complete(4, "op-2", "client:1", OpOutcome.Fail, 401));

        var today = Assert.Single(EnrollmentRules.Evaluate(history, idempotentRetry: false));
        var idempotent = Assert.Single(EnrollmentRules.Evaluate(history, idempotentRetry: true));

        Assert.Equal("E5", today.Rule);
        Assert.False(today.Required);
        Assert.Equal("E5", idempotent.Rule);
        Assert.True(idempotent.Required);
    }

    [Fact]
    public void Rules_E6_UsedByMismatch_Violation()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n-other", ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "n1"));

        var violation = Assert.Single(EnrollmentRules.Evaluate(history));

        Assert.Equal("E6", violation.Rule);
        Assert.True(violation.Required);
    }

    [Fact]
    public void Rules_E6_UnusedTokenCreatedNode_Violation()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(false, null, ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "n1"));

        Assert.Contains(EnrollmentRules.Evaluate(history), v => v.Rule == "E6" && v.Required);
    }

    [Fact]
    public void Rules_Clean_NoViolations()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n1", ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "n1"),
            EnrollmentFixtures.Invoke(3, "op-2", "client:2", "lab-r1-c1-2"), EnrollmentFixtures.Complete(4, "op-2", "client:2", OpOutcome.Fail, 401));

        Assert.Empty(EnrollmentRules.Evaluate(history));
    }
}
