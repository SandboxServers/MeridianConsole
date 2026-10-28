namespace Dhadgar.Lab.Invariants;

/// <summary>
/// The violations and check errors of every checkpoint this daemon has run (C7 <c>invariant</c> source, C9 <c>invariants.json</c>).
/// Thread-safe; a checkpoint is recorded once and read by the evaluator and the tools.
/// </summary>
public sealed class InvariantLedger
{
    private readonly object _gate = new();
    private readonly List<InvariantViolation> _violations = [];
    private readonly List<InvariantCheckError> _errors = [];
    private DateTimeOffset? _lastCheckpoint;

    /// <summary>The end time of the most recent checkpoint, or null before the first one.</summary>
    public DateTimeOffset? LastCheckpoint
    {
        get
        {
            lock (_gate)
            {
                return _lastCheckpoint;
            }
        }
    }

    /// <summary>
    /// Records one checkpoint: its violations and check errors, and the moment it ended.
    /// </summary>
    public void Record(InvariantWindow window, InvariantReport report)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            _violations.AddRange(report.Violations);
            _errors.AddRange(report.Errors);
            _lastCheckpoint = window.To;
        }
    }

    /// <summary>
    /// Every recorded violation, optionally for one invariant id and at or after <paramref name="since"/>.
    /// </summary>
    public IReadOnlyList<InvariantViolation> Violations(string? invariantId = null, DateTimeOffset? since = null)
    {
        lock (_gate)
        {
            return _violations
                .Where(v => invariantId is null || string.Equals(v.InvariantId, invariantId, StringComparison.Ordinal))
                .Where(v => since is null || v.At >= since.Value)
                .ToArray();
        }
    }

    /// <summary>
    /// Every recorded check error, optionally for one invariant id.
    /// </summary>
    public IReadOnlyList<InvariantCheckError> Errors(string? invariantId = null)
    {
        lock (_gate)
        {
            return _errors
                .Where(e => invariantId is null || string.Equals(e.InvariantId, invariantId, StringComparison.Ordinal))
                .ToArray();
        }
    }
}
