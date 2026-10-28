using System.Text.Json;
using Dhadgar.Lab.History;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// The linearizability checker on hand-written observation lists, with a trivial one-shot token model.
/// </summary>
public sealed class LinearizabilityCheckerTests
{
    [Fact]
    public void Check_TwoOkOneToken_Violation()
    {
        var history = Build(
            Invoke(1, "op-1"), Complete(2, "op-1", OpOutcome.Ok),
            Invoke(3, "op-2"), Complete(4, "op-2", OpOutcome.Ok));

        var result = LinearizabilityChecker.Check(history, new TakeOnceModel(), TimeSpan.FromSeconds(5));

        Assert.Equal(LinearizabilityChecker.Violation, result.Verdict);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("linearizability", violation.Rule);
        Assert.Equal("op-2", Assert.Single(violation.OpIds));
    }

    [Fact]
    public void Check_OkAndConcurrent401_Linearizable()
    {
        var history = Build(
            Invoke(1, "op-1"), Invoke(2, "op-2"),
            Complete(3, "op-2", OpOutcome.Fail), Complete(4, "op-1", OpOutcome.Ok));

        var result = LinearizabilityChecker.Check(history, new TakeOnceModel(), TimeSpan.FromSeconds(5));

        Assert.Equal(LinearizabilityChecker.Linearizable, result.Verdict);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Check_401BeforeAnyEffect_Violation()
    {
        var history = Build(
            Invoke(1, "op-1"), Complete(2, "op-1", OpOutcome.Fail),
            Invoke(3, "op-2"), Complete(4, "op-2", OpOutcome.Ok));

        var result = LinearizabilityChecker.Check(history, new TakeOnceModel(), TimeSpan.FromSeconds(5));

        Assert.Equal(LinearizabilityChecker.Violation, result.Verdict);
    }

    [Fact]
    public void Check_UnknownTookEffect_FailAfterIt_Linearizable()
    {
        // The unknown op never returned; the later refusal is only explained if it took effect.
        var history = Build(
            Invoke(1, "op-1"),
            Invoke(2, "op-2"), Complete(3, "op-2", OpOutcome.Fail));

        var result = LinearizabilityChecker.Check(history, new TakeOnceModel(), TimeSpan.FromSeconds(5));

        Assert.Equal(LinearizabilityChecker.Linearizable, result.Verdict);
    }

    [Fact]
    public void Check_UnknownNoEffect_Linearizable()
    {
        // The unknown op is left unlinearized: it never took effect, and the Ok op is still explained.
        var history = Build(
            Invoke(1, "op-1"),
            Invoke(2, "op-2"), Complete(3, "op-2", OpOutcome.Ok));

        var result = LinearizabilityChecker.Check(history, new TakeOnceModel(), TimeSpan.FromSeconds(5));

        Assert.Equal(LinearizabilityChecker.Linearizable, result.Verdict);
    }

    [Fact]
    public void Check_13OpsOneKey_Unknown()
    {
        var observations = new List<LabObservation>();
        for (var i = 1; i <= 13; i++)
        {
            observations.Add(Invoke(2L * i - 1, $"op-{i}"));
            observations.Add(Complete(2L * i, $"op-{i}", OpOutcome.Ok));
        }

        var result = LinearizabilityChecker.Check(Build(observations), new TakeOnceModel(), TimeSpan.FromSeconds(5));

        Assert.Equal(LinearizabilityChecker.Unknown, result.Verdict);
        Assert.Empty(result.Violations);
        Assert.Contains("history too long", result.Reason ?? string.Empty);
    }

    [Fact]
    public void Check_BudgetExceeded_Unknown()
    {
        var history = Build(
            Invoke(1, "op-1"), Complete(2, "op-1", OpOutcome.Ok),
            Invoke(3, "op-2"), Complete(4, "op-2", OpOutcome.Fail));

        var result = LinearizabilityChecker.Check(history, new TakeOnceModel(), TimeSpan.Zero);

        Assert.Equal(LinearizabilityChecker.Unknown, result.Verdict);
        Assert.Contains("budget exceeded", result.Reason ?? string.Empty);
    }

    [Fact]
    public void Minimise_SixOps_ReducesToTwo()
    {
        var observations = new List<LabObservation>();
        for (var i = 1; i <= 6; i++)
        {
            observations.Add(Invoke(2L * i - 1, $"op-{i}"));
            observations.Add(Complete(2L * i, $"op-{i}", OpOutcome.Ok));
        }

        var history = Build(observations);
        var model = new TakeOnceModel();

        var minimal = LinearizabilityChecker.Minimise(history, model, TimeSpan.FromSeconds(5));

        Assert.Equal(2, minimal.Ops.Count);
        Assert.Equal(LinearizabilityChecker.Violation, LinearizabilityChecker.Check(minimal, model, TimeSpan.FromSeconds(5)).Verdict);
    }

    private static LabObservation Invoke(long seq, string opId) =>
        new(seq, "run-1", DateTimeOffset.UnixEpoch, "actor-a", opId, ObservationKind.Invoke,
            "enroll", default, null, default, null);

    private static LabObservation Complete(long seq, string opId, OpOutcome outcome) =>
        new(seq, "run-1", DateTimeOffset.UnixEpoch, "actor-a", opId, ObservationKind.Complete,
            "enroll", default, outcome, default, null);

    private static Dhadgar.Lab.History.History Build(params LabObservation[] observations) =>
        Build(observations.ToList());

    private static Dhadgar.Lab.History.History Build(IReadOnlyList<LabObservation> observations)
    {
        var result = HistoryRecorder.FromObservations(
            "run-1", "test", observations, default(JsonElement),
            _ => ("token-1", "enroll", default(JsonElement)));
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    /// <summary>
    /// A one-shot token: the first Ok takes it; later Ok ops are refused, and Fail ops are only valid once it is taken.
    /// </summary>
    private sealed class TakeOnceModel : IHistoryModel<int>
    {
        public string Name => "take-once";

        public int Initial(string key, JsonElement finalStateForKey) => 0;

        public bool TryStep(int s, HistoryOp op, out int next)
        {
            next = s;
            switch (op.Outcome)
            {
                case OpOutcome.Ok:
                    if (s != 0)
                    {
                        return false;
                    }

                    next = 1;
                    return true;
                case OpOutcome.Fail:
                    return s != 0;
                default:
                    next = s == 0 ? 1 : s;
                    return true;
            }
        }

        public bool FinalMatches(int s, JsonElement finalStateForKey) => true;
    }
}
