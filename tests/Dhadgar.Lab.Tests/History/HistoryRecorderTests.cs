using System.Text.Json;
using Dhadgar.Lab.History;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// The recorder that pairs C18 invoke and complete lines into history ops.
/// </summary>
public sealed class HistoryRecorderTests
{
    [Fact]
    public void Recorder_InvokeWithoutComplete_Unknown()
    {
        var result = HistoryRecorder.FromObservations(
            "run-1", "enroll-race", [Invoke(1, "op-1")], default(JsonElement), Select);

        Assert.True(result.IsSuccess, result.Error);
        var op = Assert.Single(result.Value.Ops);
        Assert.Equal(OpOutcome.Unknown, op.Outcome);
        Assert.Null(op.CompleteSeq);
        Assert.Equal(1, op.InvokeSeq);
    }

    [Fact]
    public void Recorder_DoubleComplete_Malformed()
    {
        var result = HistoryRecorder.FromObservations(
            "run-1", "enroll-race",
            [Invoke(1, "op-1"), Complete(2, "op-1", OpOutcome.Ok), Complete(3, "op-1", OpOutcome.Ok)],
            default(JsonElement), Select);

        Assert.True(result.IsFailure);
        Assert.StartsWith(HistoryRecorder.MalformedCode, result.Error);
    }

    [Fact]
    public void Recorder_CompleteWithoutInvoke_Malformed()
    {
        var result = HistoryRecorder.FromObservations(
            "run-1", "enroll-race", [Complete(1, "op-9", OpOutcome.Fail)], default(JsonElement), Select);

        Assert.True(result.IsFailure);
        Assert.StartsWith(HistoryRecorder.MalformedCode, result.Error);
    }

    private static (string Key, string F, JsonElement Input)? Select(LabObservation o) =>
        ("token-1", "enroll", default(JsonElement));

    private static LabObservation Invoke(long seq, string opId) =>
        new(seq, "run-1", DateTimeOffset.UnixEpoch, "actor-a", opId, ObservationKind.Invoke,
            "enroll", default, null, default, null);

    private static LabObservation Complete(long seq, string opId, OpOutcome outcome) =>
        new(seq, "run-1", DateTimeOffset.UnixEpoch, "actor-a", opId, ObservationKind.Complete,
            "enroll", default, outcome, default, null);
}
