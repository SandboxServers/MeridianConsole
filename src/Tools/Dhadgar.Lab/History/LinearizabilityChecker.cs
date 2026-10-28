using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.History;

/// <summary>
/// Wing-Gong-Lowe linearizability search (C19), run per key. Memoises visited (linearized set, state) pairs.
/// Deterministic: ops are visited in InvokeSeq order and output never depends on hash iteration order.
/// Never a false pass: a budget overrun or a key with more than <see cref="MaxOpsPerKey"/> ops is <c>unknown</c>.
/// </summary>
public static class LinearizabilityChecker
{
    /// <summary>The verdict when a linearization exists.</summary>
    public const string Linearizable = "linearizable";

    /// <summary>The verdict when no linearization exists.</summary>
    public const string Violation = "violation";

    /// <summary>The verdict when the search could not finish. Never a pass.</summary>
    public const string Unknown = "unknown";

    /// <summary>The rule id of linearizability violations.</summary>
    public const string LinearizabilityRule = "linearizability";

    /// <summary>The largest number of ops on one key the search attempts.</summary>
    public const int MaxOpsPerKey = 12;

    private const int BudgetPollMask = 1023;

    /// <summary>
    /// Checks <paramref name="history"/> against <paramref name="model"/>, per key, within <paramref name="budget"/>.
    /// </summary>
    /// <returns>The verdict. A violation dominates an unknown key.</returns>
    public static CheckResult Check<TState>(History history, IHistoryModel<TState> model, TimeSpan budget)
        where TState : IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(model);
        return CheckWithDeadline(history, model, DeadlineAfter(budget));
    }

    /// <summary>
    /// Minimises a failing history (delta debugging over the Ok/Fail ops; Unknown ops are kept) within
    /// <paramref name="budget"/>. Returns the smallest failing history found, or <paramref name="failing"/> when it
    /// does not check as a violation.
    /// </summary>
    public static History Minimise<TState>(History failing, IHistoryModel<TState> model, TimeSpan budget)
        where TState : IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(failing);
        ArgumentNullException.ThrowIfNull(model);
        return Minimiser.Minimise(failing, model, DeadlineAfter(budget));
    }

    internal static long DeadlineAfter(TimeSpan budget) =>
        Stopwatch.GetTimestamp() + (long)(Math.Max(0, budget.TotalSeconds) * Stopwatch.Frequency);

    internal static CheckResult CheckWithDeadline<TState>(History history, IHistoryModel<TState> model, long deadline)
        where TState : IEquatable<TState>
    {
        var violations = new List<HistoryViolation>();
        var reasons = new List<string>();

        foreach (var (key, ops) in GroupByKey(history.Ops))
        {
            if (ops.Count > MaxOpsPerKey)
            {
                reasons.Add($"key {key}: history too long ({ops.Count} ops)");
                continue;
            }

            var search = new KeySearch<TState>(key, ops, history.FinalState, model, deadline);
            if (search.Run())
            {
                continue;
            }

            if (search.TimedOut)
            {
                reasons.Add($"key {key}: budget exceeded");
            }
            else
            {
                violations.Add(search.Violation());
            }
        }

        if (violations.Count > 0)
        {
            return new CheckResult(Violation, violations, null, reasons.Count == 0 ? null : string.Join("; ", reasons));
        }

        if (reasons.Count > 0)
        {
            return new CheckResult(Unknown, [], null, string.Join("; ", reasons));
        }

        return new CheckResult(Linearizable, [], null);
    }

    private static List<(string Key, List<HistoryOp> Ops)> GroupByKey(IReadOnlyList<HistoryOp> ops)
    {
        var index = new Dictionary<string, List<HistoryOp>>(StringComparer.Ordinal);
        var groups = new List<(string Key, List<HistoryOp> Ops)>();
        foreach (var op in ops.OrderBy(o => o.InvokeSeq))
        {
            if (!index.TryGetValue(op.Key, out var list))
            {
                list = [];
                index[op.Key] = list;
                groups.Add((op.Key, list));
            }

            list.Add(op);
        }

        return groups;
    }

    /// <summary>
    /// The search for one key. The state of the search is (bitset of linearized ops, model state).
    /// </summary>
    private sealed class KeySearch<TState> where TState : IEquatable<TState>
    {
        private readonly string _key;
        private readonly List<HistoryOp> _ops;
        private readonly JsonElement _final;
        private readonly IHistoryModel<TState> _model;
        private readonly long _deadline;
        private readonly HashSet<(ulong Mask, TState State)> _visited = [];
        private readonly ulong _completeMask;
        private readonly TState _initial;
        private int _steps;
        private ulong _bestMask;
        private int _bestCount = -1;

        public KeySearch(string key, List<HistoryOp> ops, JsonElement final, IHistoryModel<TState> model, long deadline)
        {
            _key = key;
            _ops = ops;
            _final = final;
            _model = model;
            _deadline = deadline;
            for (var i = 0; i < ops.Count; i++)
            {
                if (ops[i].Outcome != OpOutcome.Unknown)
                {
                    _completeMask |= 1UL << i;
                }
            }

            _initial = model.Initial(key, final);
        }

        /// <summary>True when the search ran out of budget.</summary>
        public bool TimedOut { get; private set; }

        /// <summary>True when a linearization exists.</summary>
        public bool Run() => Dfs(0UL, _initial);

        /// <summary>The violation for a key with no linearization: the complete ops the deepest search state could not order.</summary>
        public HistoryViolation Violation()
        {
            var unordered = new List<string>();
            for (var i = 0; i < _ops.Count; i++)
            {
                if (_ops[i].Outcome != OpOutcome.Unknown && (_bestMask & (1UL << i)) == 0)
                {
                    unordered.Add(_ops[i].OpId);
                }
            }

            var detail = $"no linearization of key {_key} with model {_model.Name}; " +
                         $"the longest ordered prefix had {_bestCount} of {_ops.Count} ops";
            return new HistoryViolation(LinearizabilityRule, unordered, detail);
        }

        private bool Dfs(ulong mask, TState state)
        {
            if ((_steps++ & BudgetPollMask) == 0 && Stopwatch.GetTimestamp() >= _deadline)
            {
                TimedOut = true;
                return false;
            }

            if ((mask & _completeMask) == _completeMask && _model.FinalMatches(state, _final))
            {
                return true;
            }

            var count = BitOperations.PopCount(mask);
            if (count > _bestCount)
            {
                _bestCount = count;
                _bestMask = mask;
            }

            if (!_visited.Add((mask, state)))
            {
                return false;
            }

            for (var i = 0; i < _ops.Count; i++)
            {
                var bit = 1UL << i;
                if ((mask & bit) != 0 || !IsMinimal(i, mask))
                {
                    continue;
                }

                if (!_model.TryStep(state, _ops[i], out var next))
                {
                    continue;
                }

                if (Dfs(mask | bit, next))
                {
                    return true;
                }

                if (TimedOut)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Op <paramref name="i"/> may be linearized next when no pending complete op returned before it was invoked.
        /// Unknown ops never return before anything is invoked (their return is +infinity).
        /// </summary>
        private bool IsMinimal(int i, ulong mask)
        {
            var invokeSeq = _ops[i].InvokeSeq;
            for (var j = 0; j < _ops.Count; j++)
            {
                if (j == i || (mask & (1UL << j)) != 0)
                {
                    continue;
                }

                if (_ops[j].CompleteSeq is long completeSeq && completeSeq < invokeSeq)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
