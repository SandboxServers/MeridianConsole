using System.Diagnostics;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.History;

/// <summary>
/// Delta debugging (ddmin) over the Ok/Fail ops of a failing history (C19). Unknown ops are always kept.
/// Every candidate is re-checked; a candidate is accepted only when the check says <c>violation</c>, so the result
/// always fails. Deterministic: chunks are tried in InvokeSeq order. Stops at the deadline.
/// </summary>
internal static class Minimiser
{
    /// <summary>
    /// Returns the smallest failing history found before <paramref name="deadline"/>, or <paramref name="failing"/>
    /// itself when it does not check as a violation.
    /// </summary>
    internal static History Minimise<TState>(History failing, IHistoryModel<TState> model, long deadline)
        where TState : IEquatable<TState>
    {
        if (LinearizabilityChecker.CheckWithDeadline(failing, model, deadline).Verdict != LinearizabilityChecker.Violation)
        {
            return failing;
        }

        var unknowns = failing.Ops.Where(o => o.Outcome == OpOutcome.Unknown).ToList();
        var current = failing.Ops.Where(o => o.Outcome != OpOutcome.Unknown).OrderBy(o => o.InvokeSeq).ToList();
        var granularity = 2;

        while (current.Count >= 2 && Stopwatch.GetTimestamp() < deadline)
        {
            granularity = Math.Min(granularity, current.Count);
            var reduced = false;

            // Reduce to one chunk.
            for (var k = 0; k < granularity && !reduced; k++)
            {
                var (from, to) = Range(current.Count, granularity, k);
                var subset = current.GetRange(from, to - from);
                if (subset.Count < current.Count && Fails(failing, subset, unknowns, model, deadline))
                {
                    current = subset;
                    granularity = 2;
                    reduced = true;
                }
            }

            // Reduce by removing one chunk.
            for (var k = 0; k < granularity && !reduced; k++)
            {
                var (from, to) = Range(current.Count, granularity, k);
                var complement = current.Take(from).Concat(current.Skip(to)).ToList();
                if (complement.Count > 0 && complement.Count < current.Count
                    && Fails(failing, complement, unknowns, model, deadline))
                {
                    current = complement;
                    granularity = Math.Max(granularity - 1, 2);
                    reduced = true;
                }
            }

            if (!reduced)
            {
                if (granularity >= current.Count)
                {
                    break;
                }

                granularity = Math.Min(granularity * 2, current.Count);
            }
        }

        return Build(failing, current, unknowns);
    }

    private static (int From, int To) Range(int count, int granularity, int chunk) =>
        (chunk * count / granularity, (chunk + 1) * count / granularity);

    private static bool Fails<TState>(
        History failing,
        List<HistoryOp> subset,
        List<HistoryOp> unknowns,
        IHistoryModel<TState> model,
        long deadline)
        where TState : IEquatable<TState>
    {
        var candidate = Build(failing, subset, unknowns);
        return LinearizabilityChecker.CheckWithDeadline(candidate, model, deadline).Verdict == LinearizabilityChecker.Violation;
    }

    private static History Build(History failing, List<HistoryOp> complete, List<HistoryOp> unknowns)
    {
        var ops = complete.Concat(unknowns).OrderBy(o => o.InvokeSeq).ToList();
        return new History(failing.RunId, failing.Scenario, ops, failing.FinalState);
    }
}
