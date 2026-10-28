using System.Text.Json;
using Dhadgar.Lab.Observe;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.History;

/// <summary>
/// Builds a <see cref="History"/> from C18 observation lines by pairing <c>Invoke</c> and <c>Complete</c> lines on their op id.
/// </summary>
public static class HistoryRecorder
{
    /// <summary>The error code for observation lines that cannot form a history.</summary>
    public const string MalformedCode = "[Lab.HistoryMalformed]";

    /// <summary>
    /// Pairs the invoke and complete lines of the selected ops. An invoke with no complete becomes an <c>Unknown</c> op.
    /// A complete with no invoke, two completes for one op, or a complete before its invoke is malformed.
    /// </summary>
    /// <param name="runId">The run the observations belong to.</param>
    /// <param name="scenario">The scenario id (C19).</param>
    /// <param name="obs">The observation lines, in any order; they are ordered by <see cref="LabObservation.Seq"/>.</param>
    /// <param name="finalState">The db_query snapshot taken at run end.</param>
    /// <param name="select">Returns the key, operation name and input of an invoke line that is a history op; <c>null</c> skips it.</param>
    /// <returns>The history, or a failure with <see cref="MalformedCode"/>.</returns>
    public static Result<History> FromObservations(
        string runId,
        string scenario,
        IReadOnlyList<LabObservation> obs,
        JsonElement finalState,
        Func<LabObservation, (string Key, string F, JsonElement Input)?> select)
    {
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(obs);
        ArgumentNullException.ThrowIfNull(select);

        var invokes = new Dictionary<string, (LabObservation Invoke, (string Key, string F, JsonElement Input) Sel)>(StringComparer.Ordinal);
        var invokeOrder = new List<string>();
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        var completes = new Dictionary<string, LabObservation>(StringComparer.Ordinal);

        foreach (var o in obs.OrderBy(o => o.Seq))
        {
            if (o.Kind == ObservationKind.Invoke)
            {
                if (string.IsNullOrEmpty(o.OpId))
                {
                    return Malformed("invoke line without an op id");
                }

                if (invokes.ContainsKey(o.OpId) || ignored.Contains(o.OpId))
                {
                    return Malformed($"op {o.OpId} was invoked twice");
                }

                var sel = select(o);
                if (sel is null)
                {
                    ignored.Add(o.OpId);
                    continue;
                }

                invokes[o.OpId] = (o, sel.Value);
                invokeOrder.Add(o.OpId);
            }
            else if (o.Kind == ObservationKind.Complete)
            {
                if (string.IsNullOrEmpty(o.OpId))
                {
                    return Malformed("complete line without an op id");
                }

                if (ignored.Contains(o.OpId))
                {
                    continue;
                }

                if (!invokes.TryGetValue(o.OpId, out var pending))
                {
                    return Malformed($"op {o.OpId} completed without an invoke");
                }

                if (completes.ContainsKey(o.OpId))
                {
                    return Malformed($"op {o.OpId} completed twice");
                }

                if (o.Seq < pending.Invoke.Seq)
                {
                    return Malformed($"op {o.OpId} completed before it was invoked");
                }

                if (o.Outcome is null)
                {
                    return Malformed($"op {o.OpId} completed without an outcome");
                }

                completes[o.OpId] = o;
            }
        }

        var ops = new List<HistoryOp>(invokeOrder.Count);
        foreach (var id in invokeOrder)
        {
            var (inv, sel) = invokes[id];
            if (completes.TryGetValue(id, out var c))
            {
                var outcome = c.Outcome!.Value;
                ops.Add(new HistoryOp(
                    id, inv.Actor, sel.Key, sel.F, sel.Input, inv.Seq,
                    outcome == OpOutcome.Unknown ? null : c.Seq,
                    outcome,
                    c.Result));
            }
            else
            {
                ops.Add(new HistoryOp(
                    id, inv.Actor, sel.Key, sel.F, sel.Input, inv.Seq,
                    null, OpOutcome.Unknown, default(JsonElement)));
            }
        }

        return Result<History>.Success(new History(runId, scenario, ops, finalState));
    }

    private static Result<History> Malformed(string detail) =>
        Result<History>.Failure($"{MalformedCode} {detail}");
}
