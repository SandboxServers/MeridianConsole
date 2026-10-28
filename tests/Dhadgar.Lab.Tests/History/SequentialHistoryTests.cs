using CsCheck;
using Dhadgar.Lab.History;
using Dhadgar.Lab.History.Models;
using Dhadgar.Lab.History.Rules;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// Property tests: a history that applies the enrollment model sequentially (non-overlapping intervals) is always
/// linearizable, and satisfies E1-E6.
/// </summary>
public sealed class SequentialHistoryTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Fact]
    public void SequentialHistories_AlwaysLinearizable()
    {
        Gen.Bool.Array[1, 10].Sample(actorBits =>
        {
            AssertSequentialLinearizable(idempotent: false, actorBits);
            AssertSequentialLinearizable(idempotent: true, actorBits);
        }, iter: 200);
    }

    /// <summary>
    /// Applies one enroll per entry of <paramref name="actorBits"/>, in order, each invoked and completed before the next.
    /// The first enroll wins; later ones are refused, or (idempotent model) return the winner's node for the same key.
    /// </summary>
    private static void AssertSequentialLinearizable(bool idempotent, bool[] actorBits)
    {
        var observations = new List<LabObservation>();
        long seq = 0;
        string? winnerNode = null;
        string winnerHost = string.Empty;
        string winnerActor = string.Empty;

        for (var i = 0; i < actorBits.Length; i++)
        {
            var actor = actorBits[i] ? "client:1" : "client:0";
            var opId = $"op-{i}";
            var host = $"lab-r1-c1-{i}";
            observations.Add(EnrollmentFixtures.Invoke(++seq, opId, actor, host));

            if (winnerNode is null)
            {
                winnerNode = $"node-{i}";
                winnerHost = host;
                winnerActor = actor;
                observations.Add(EnrollmentFixtures.Complete(++seq, opId, actor, OpOutcome.Ok, 201, winnerNode));
            }
            else if (idempotent && actor == winnerActor)
            {
                observations.Add(EnrollmentFixtures.Complete(++seq, opId, actor, OpOutcome.Ok, 201, winnerNode));
            }
            else
            {
                observations.Add(EnrollmentFixtures.Complete(++seq, opId, actor, OpOutcome.Fail, 401));
            }
        }

        var nodes = winnerNode is null
            ? Array.Empty<(string Id, string Name, int ActiveCerts)>()
            : new[] { (winnerNode, winnerHost, 1) };
        var history = EnrollmentFixtures.Build(
            EnrollmentFixtures.FinalState(winnerNode is not null, winnerNode, nodes),
            observations.ToArray());

        var result = LinearizabilityChecker.Check(history, new EnrollmentHistoryModel(idempotent), Budget);

        Assert.Equal(LinearizabilityChecker.Linearizable, result.Verdict);
        var violations = EnrollmentRules.Evaluate(history, idempotent);
        Assert.True(violations.Count == 0, string.Join("; ", violations.Select(v => $"{v.Rule} {v.Detail}")));
    }
}
