using System.Text.Json;
using Dhadgar.Lab.History.Models;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.History.Rules;

/// <summary>
/// The enrollment rules E1-E6 (C19) over a history and its final state (one token per run). Each violation carries
/// its <see cref="HistoryViolation.Required"/> flag: E5 is required only when the model is idempotent (D-ML49).
/// Deterministic: ops are visited in InvokeSeq order.
/// </summary>
public static class EnrollmentRules
{
    /// <summary>
    /// Evaluates E1-E6 for <paramref name="h"/>.
    /// </summary>
    /// <param name="h">The history, with its db_query final state.</param>
    /// <param name="idempotentRetry">Whether the model accepts a same-key retry after success (D-ML49); E5 is required only then.</param>
    /// <returns>The violations in rule order. Empty when every rule holds.</returns>
    public static IReadOnlyList<HistoryViolation> Evaluate(History h, bool idempotentRetry = false)
    {
        ArgumentNullException.ThrowIfNull(h);
        var ops = h.Ops.OrderBy(o => o.InvokeSeq).ToList();
        var nodes = EnrollmentHistoryModel.NodeRows(h.FinalState);
        var token = EnrollmentHistoryModel.Prop(h.FinalState, "token");
        var violations = new List<HistoryViolation>();

        CheckAtMostOneNode(ops, violations);
        CheckActiveCertificates(ops, nodes, violations);
        CheckFailedMadeNoNode(ops, nodes, violations);
        CheckRefusedBeforeEffect(ops, nodes, violations);
        CheckNoOrphan(ops, nodes, idempotentRetry, violations);
        CheckUsedBy(ops, nodes, token, violations);
        return violations;
    }

    /// <summary>E1: at most one Ok per token. A retry that returns the node already created is the same node.</summary>
    private static void CheckAtMostOneNode(IReadOnlyList<HistoryOp> ops, List<HistoryViolation> violations)
    {
        var oks = ops.Where(o => o.Outcome == OpOutcome.Ok).ToList();
        var distinct = oks.Select(o => EnrollmentHistoryModel.NodeIdOf(o) ?? o.OpId).Distinct(StringComparer.Ordinal).Count();
        if (distinct > 1)
        {
            Add(violations, "E1", oks.Select(o => o.OpId).ToList(),
                $"{distinct} distinct nodes were created for one token by Ok enrolls", required: true);
        }
    }

    /// <summary>E2: every Ok node exists at the end of the run with exactly one active certificate.</summary>
    private static void CheckActiveCertificates(IReadOnlyList<HistoryOp> ops, IReadOnlyList<JsonElement> nodes, List<HistoryViolation> violations)
    {
        foreach (var op in ops.Where(o => o.Outcome == OpOutcome.Ok))
        {
            var node = FindNodeForOk(op, nodes);
            if (node is null)
            {
                Add(violations, "E2", [op.OpId], $"Ok node {EnrollmentHistoryModel.NodeNameOf(EnrollmentHistoryModel.Hostname(op))} is missing at the end of the run", required: true);
                continue;
            }

            var certs = EnrollmentHistoryModel.Prop(node.Value, "active_certs");
            if (certs.ValueKind != JsonValueKind.Number || !certs.TryGetInt32(out var count) || count != 1)
            {
                Add(violations, "E2", [op.OpId], $"Ok node has {Describe(certs)} active certificates, expected 1", required: true);
            }
        }
    }

    /// <summary>E3: a definite failure made no node row.</summary>
    private static void CheckFailedMadeNoNode(IReadOnlyList<HistoryOp> ops, IReadOnlyList<JsonElement> nodes, List<HistoryViolation> violations)
    {
        foreach (var op in ops.Where(o => o.Outcome == OpOutcome.Fail))
        {
            if (FindNodeByHostname(op, nodes) is not null)
            {
                Add(violations, "E3", [op.OpId], $"a definite failure made node {EnrollmentHistoryModel.NodeNameOf(EnrollmentHistoryModel.Hostname(op))}", required: true);
            }
        }
    }

    /// <summary>
    /// E4: no 401 whose Complete precedes the Invoke of the op that took effect. That op is the earliest-invoked
    /// winner (an Ok, or an Unknown whose node exists); a later same-key retry that returns the same node did not take effect.
    /// </summary>
    private static void CheckRefusedBeforeEffect(IReadOnlyList<HistoryOp> ops, IReadOnlyList<JsonElement> nodes, List<HistoryViolation> violations)
    {
        var effective = ops.FirstOrDefault(o => o.Outcome == OpOutcome.Ok || (o.Outcome == OpOutcome.Unknown && FindNodeByHostname(o, nodes) is not null));
        if (effective is null)
        {
            return;
        }

        foreach (var refusal in ops.Where(o => o.Outcome == OpOutcome.Fail && EnrollmentHistoryModel.Status(o) == 401))
        {
            if (refusal.CompleteSeq is long refusedAt && refusedAt < effective.InvokeSeq)
            {
                Add(violations, "E4", [refusal.OpId, effective.OpId], "a 401 was returned before the token was used by a later enroll", required: true);
            }
        }
    }

    /// <summary>
    /// E5: after an Unknown op that took effect (its node exists), a retry by the same actor with the same key leaves the
    /// actor with a node it holds a certificate for. Violated when every retry is a definite failure.
    /// </summary>
    private static void CheckNoOrphan(IReadOnlyList<HistoryOp> ops, IReadOnlyList<JsonElement> nodes, bool idempotentRetry, List<HistoryViolation> violations)
    {
        foreach (var unknown in ops.Where(o => o.Outcome == OpOutcome.Unknown && FindNodeByHostname(o, nodes) is not null))
        {
            var retries = ops.Where(o => o.Actor == unknown.Actor && o.Key == unknown.Key && o.InvokeSeq > unknown.InvokeSeq).ToList();
            if (retries.Count == 0 || !retries.All(r => r.Outcome == OpOutcome.Fail))
            {
                continue;
            }

            var ids = new List<string> { unknown.OpId };
            ids.AddRange(retries.Select(r => r.OpId));
            Add(violations, "E5", ids,
                $"an Unknown enroll created node {EnrollmentHistoryModel.NodeNameOf(EnrollmentHistoryModel.Hostname(unknown))} and the retry was refused: the actor holds no node (orphan)",
                required: idempotentRetry);
        }
    }

    /// <summary>E6: a used token's used_by_node_id is the one node its ops created; an unused token created no node.</summary>
    private static void CheckUsedBy(IReadOnlyList<HistoryOp> ops, IReadOnlyList<JsonElement> nodes, JsonElement token, List<HistoryViolation> violations)
    {
        var hostnames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in ops)
        {
            var name = EnrollmentHistoryModel.NodeNameOf(EnrollmentHistoryModel.Hostname(op));
            if (name.Length > 0)
            {
                hostnames.Add(name);
            }
        }

        var created = nodes.Where(n => hostnames.Contains(EnrollmentHistoryModel.Text(EnrollmentHistoryModel.Prop(n, "name")) ?? string.Empty)).ToList();
        var createdIds = ops.Where(o => FindNodeByHostname(o, nodes) is not null).Select(o => o.OpId).ToList();
        var usedBy = EnrollmentHistoryModel.Text(EnrollmentHistoryModel.Prop(token, "used_by_node_id"));

        if (EnrollmentHistoryModel.HasValue(EnrollmentHistoryModel.Prop(token, "used_at")))
        {
            var createdId = created.Count == 1 ? EnrollmentHistoryModel.Text(EnrollmentHistoryModel.Prop(created[0], "id")) : null;
            if (createdId is null || usedBy != createdId)
            {
                Add(violations, "E6", createdIds,
                    $"the used token's used_by_node_id {usedBy ?? "(none)"} is not the one node its ops created ({created.Count} created)", required: true);
            }
        }
        else if (created.Count > 0)
        {
            Add(violations, "E6", createdIds, $"an unused token created {created.Count} node(s)", required: true);
        }
    }

    /// <summary>The node an Ok op refers to: by node id when the output carries one (a D-ML49 retry returns the first node), else by hostname.</summary>
    private static JsonElement? FindNodeForOk(HistoryOp op, IReadOnlyList<JsonElement> nodes)
    {
        var nodeId = EnrollmentHistoryModel.NodeIdOf(op);
        if (nodeId is null)
        {
            return FindNodeByHostname(op, nodes);
        }

        foreach (var node in nodes)
        {
            if (EnrollmentHistoryModel.Text(EnrollmentHistoryModel.Prop(node, "id")) == nodeId)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>The node whose name is the op's sanitised hostname, or <c>null</c>.</summary>
    private static JsonElement? FindNodeByHostname(HistoryOp op, IReadOnlyList<JsonElement> nodes)
    {
        var name = EnrollmentHistoryModel.NodeNameOf(EnrollmentHistoryModel.Hostname(op));
        if (name.Length == 0)
        {
            return null;
        }

        foreach (var node in nodes)
        {
            if (EnrollmentHistoryModel.Text(EnrollmentHistoryModel.Prop(node, "name")) == name)
            {
                return node;
            }
        }

        return null;
    }

    private static string Describe(JsonElement value) => EnrollmentHistoryModel.Text(value) ?? "no";

    private static void Add(List<HistoryViolation> violations, string rule, IReadOnlyList<string> opIds, string detail, bool required) =>
        violations.Add(new HistoryViolation(rule, opIds, detail, required));
}
