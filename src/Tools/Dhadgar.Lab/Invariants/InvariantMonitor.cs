using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Invariants;

/// <summary>
/// Runs every registered invariant over one window, in parallel, each with a 30 s timeout. One failing check becomes an
/// <see cref="InvariantCheckError"/>; the others still run (C11).
/// </summary>
public sealed class InvariantMonitor
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<IInvariant> _invariants;

    /// <summary>
    /// Creates a monitor over the given invariants.
    /// </summary>
    public InvariantMonitor(IEnumerable<IInvariant> invariants)
    {
        _invariants = invariants.ToArray();
    }

    /// <summary>
    /// Runs every invariant over the window and gathers the violations and the check errors.
    /// </summary>
    public async Task<InvariantReport> CheckpointAsync(InvariantWindow window, CancellationToken ct)
    {
        var tasks = _invariants.Select(invariant => RunOneAsync(invariant, window, ct)).ToArray();
        var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);

        var violations = new List<InvariantViolation>();
        var errors = new List<InvariantCheckError>();
        var skipped = new List<InvariantSkip>();
        foreach (CheckOutcome outcome in outcomes)
        {
            if (outcome.Error is null)
            {
                violations.AddRange(outcome.Violations);
                skipped.AddRange(outcome.Skipped);
            }
            else
            {
                errors.Add(new InvariantCheckError(outcome.InvariantId, outcome.Error));
            }
        }

        return new InvariantReport(violations, errors, skipped);
    }

    private static async Task<CheckOutcome> RunOneAsync(IInvariant invariant, InvariantWindow window, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            if (invariant is ISkippableInvariant skippable)
            {
                Result<InvariantOutcome> withSkips = await skippable.CheckWithSkipsAsync(window, timeout.Token).ConfigureAwait(false);
                return withSkips.IsSuccess
                    ? new CheckOutcome(invariant.Id, withSkips.Value.Violations, withSkips.Value.Skipped.Select(s => s with { Row = window.Row }).ToArray(), null)
                    : new CheckOutcome(invariant.Id, Array.Empty<InvariantViolation>(), Array.Empty<InvariantSkip>(), withSkips.Error);
            }

            Result<IReadOnlyList<InvariantViolation>> result =
                await invariant.CheckAsync(window, timeout.Token).ConfigureAwait(false);
            return result.IsSuccess
                ? new CheckOutcome(invariant.Id, result.Value, Array.Empty<InvariantSkip>(), null)
                : new CheckOutcome(invariant.Id, Array.Empty<InvariantViolation>(), Array.Empty<InvariantSkip>(), result.Error);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CheckOutcome(invariant.Id, Array.Empty<InvariantViolation>(), Array.Empty<InvariantSkip>(), "timed out after 30 s");
        }
        catch (Exception ex)
        {
            // The exception message is not copied: it may carry a secret.
            return new CheckOutcome(invariant.Id, Array.Empty<InvariantViolation>(), Array.Empty<InvariantSkip>(), "check threw " + ex.GetType().Name);
        }
    }

    private sealed record CheckOutcome(string InvariantId, IReadOnlyList<InvariantViolation> Violations, IReadOnlyList<InvariantSkip> Skipped, string? Error);
}
