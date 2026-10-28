using System.Text.Json;
using Dhadgar.Lab.History;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.History;

/// <summary>
/// Hand-written enrollment observations: one token (<c>token-1</c>), enroll ops, and db_query style final states.
/// </summary>
internal static class EnrollmentFixtures
{
    public const string RunId = "run-1";
    public const string TokenId = "token-1";

    /// <summary>The invoke line of an enroll op; the input carries the hostname and the public key, never a token.</summary>
    public static LabObservation Invoke(long seq, string opId, string actor, string hostname) =>
        new(seq, RunId, DateTimeOffset.UnixEpoch, actor, opId, ObservationKind.Invoke, "enroll",
            JsonSerializer.SerializeToElement(new { hostname, public_key = "pk-" + actor }), null, default, null);

    /// <summary>The complete line of an enroll op with its status and optional node id.</summary>
    public static LabObservation Complete(long seq, string opId, string actor, OpOutcome outcome, int status, string? nodeId = null) =>
        new(seq, RunId, DateTimeOffset.UnixEpoch, actor, opId, ObservationKind.Complete, "enroll", default, outcome,
            JsonSerializer.SerializeToElement(new { status, node_id = nodeId }), null);

    /// <summary>The db_query final state: the token row and the run's node rows.</summary>
    public static JsonElement FinalState(bool used, string? usedByNodeId, params (string Id, string Name, int ActiveCerts)[] nodes) =>
        JsonSerializer.SerializeToElement(new
        {
            token = new
            {
                id = TokenId,
                used_at = used ? "2026-01-01T00:00:00Z" : null,
                used_by_node_id = usedByNodeId,
                revoked = false,
            },
            nodes = nodes.Select(n => new { id = n.Id, name = n.Name, active_certs = n.ActiveCerts }).ToArray(),
        });

    /// <summary>Builds the history through the recorder, failing the test when the observations are malformed.</summary>
    public static Dhadgar.Lab.History.History Build(JsonElement finalState, params LabObservation[] observations)
    {
        var result = HistoryRecorder.FromObservations(RunId, "enroll-test", observations.ToList(), finalState, Select);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static (string Key, string F, JsonElement Input)? Select(LabObservation o) =>
        o.Op == "enroll" ? (TokenId, "enroll", o.Args) : null;
}
