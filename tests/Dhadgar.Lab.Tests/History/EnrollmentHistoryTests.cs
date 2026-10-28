using System.Text.Json;
using Dhadgar.Lab.History;
using Dhadgar.Lab.History.Models;
using Dhadgar.Lab.History.Rules;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// The linearizability checker with the enrollment model (C19) on hand-written enrollment histories.
/// </summary>
public sealed class EnrollmentHistoryTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Fact]
    public void Check_TwoOkOneToken_Violation()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n1", ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "n1"),
            EnrollmentFixtures.Invoke(3, "op-2", "client:2", "lab-r1-c1-2"), EnrollmentFixtures.Complete(4, "op-2", "client:2", OpOutcome.Ok, 201, "n2"));

        var result = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(false), Budget);

        Assert.Equal(LinearizabilityChecker.Violation, result.Verdict);
        Assert.Equal(LinearizabilityChecker.LinearizabilityRule, Assert.Single(result.Violations).Rule);
    }

    [Fact]
    public void Check_OkAndConcurrent401_Linearizable()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n1", ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Invoke(2, "op-2", "client:2", "lab-r1-c1-2"),
            EnrollmentFixtures.Complete(3, "op-2", "client:2", OpOutcome.Fail, 401), EnrollmentFixtures.Complete(4, "op-1", "client:1", OpOutcome.Ok, 201, "n1"));

        var result = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(false), Budget);

        Assert.Equal(LinearizabilityChecker.Linearizable, result.Verdict);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Check_401BeforeAnyEffect_Violation()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n2", ("n2", "lab-r1-c1-2", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Fail, 401),
            EnrollmentFixtures.Invoke(3, "op-2", "client:2", "lab-r1-c1-2"), EnrollmentFixtures.Complete(4, "op-2", "client:2", OpOutcome.Ok, 201, "n2"));

        var result = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(false), Budget);

        Assert.Equal(LinearizabilityChecker.Violation, result.Verdict);
    }

    [Fact]
    public void Check_UnknownTookEffect_RetryGets401_Linearizable_E5Orphan()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n1", ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"),
            EnrollmentFixtures.Invoke(3, "op-2", "client:1", "lab-r1-c1-2"), EnrollmentFixtures.Complete(4, "op-2", "client:1", OpOutcome.Fail, 401));

        var result = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(false), Budget);
        var e5 = Assert.Single(EnrollmentRules.Evaluate(history, idempotentRetry: false));

        Assert.Equal(LinearizabilityChecker.Linearizable, result.Verdict);
        Assert.Equal("E5", e5.Rule);
        Assert.False(e5.Required);
    }

    [Fact]
    public void Check_UnknownTookEffect_IdempotentModel_RetryOk_Linearizable()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n1", ("n1", "lab-r1-c1-1", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"),
            EnrollmentFixtures.Invoke(3, "op-2", "client:1", "lab-r1-c1-2"), EnrollmentFixtures.Complete(4, "op-2", "client:1", OpOutcome.Ok, 201, "n1"));

        var idempotent = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(true), Budget);
        var today = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(false), Budget);

        Assert.Equal(LinearizabilityChecker.Linearizable, idempotent.Verdict);
        Assert.Empty(EnrollmentRules.Evaluate(history, idempotentRetry: true));
        Assert.Equal(LinearizabilityChecker.Violation, today.Verdict);
    }

    [Fact]
    public void Check_UnknownNoEffect_Linearizable()
    {
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(true, "n2", ("n2", "lab-r1-c1-2", 1)),
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"),
            EnrollmentFixtures.Invoke(2, "op-2", "client:2", "lab-r1-c1-2"), EnrollmentFixtures.Complete(3, "op-2", "client:2", OpOutcome.Ok, 201, "n2"));

        var result = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(false), Budget);

        Assert.Equal(LinearizabilityChecker.Linearizable, result.Verdict);
        Assert.Empty(EnrollmentRules.Evaluate(history));
    }

    [Fact]
    public void Check_RevokedToken_Ok_Violation()
    {
        var revoked = JsonSerializer.SerializeToElement(new
        {
            token = new { id = EnrollmentFixtures.TokenId, used_at = (string?)null, used_by_node_id = (string?)null, revoked = true },
            nodes = Array.Empty<object>(),
        });
        var history = EnrollmentFixtures.Build(
            revoked,
            EnrollmentFixtures.Invoke(1, "op-1", "client:1", "lab-r1-c1-1"), EnrollmentFixtures.Complete(2, "op-1", "client:1", OpOutcome.Ok, 201, "n1"));

        var result = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(false), Budget);

        Assert.Equal(LinearizabilityChecker.Violation, result.Verdict);
    }
}
