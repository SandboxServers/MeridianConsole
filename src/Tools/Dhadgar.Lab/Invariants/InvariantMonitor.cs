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
        foreach (CheckOutcome outcome in outcomes)
        {
            if (outcome.Error is null)
            {
                violations.AddRange(outcome.Violations);
            }
            else
            {
                errors.Add(new InvariantCheckError(outcome.InvariantId, outcome.Error));
            }
        }

        return new InvariantReport(violations, errors);
    }

    private static async Task<CheckOutcome> RunOneAsync(IInvariant invariant, InvariantWindow window, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            Result<IReadOnlyList<InvariantViolation>> result =
                await invariant.CheckAsync(window, timeout.Token).ConfigureAwait(false);
            return result.IsSuccess
                ? new CheckOutcome(invariant.Id, result.Value, null)
                : new CheckOutcome(invariant.Id, Array.Empty<InvariantViolation>(), result.Error);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CheckOutcome(invariant.Id, Array.Empty<InvariantViolation>(), "timed out after 30 s");
        }
        catch (Exception ex)
        {
            // The exception message is not copied: it may carry a secret.
            return new CheckOutcome(invariant.Id, Array.Empty<InvariantViolation>(), "check threw " + ex.GetType().Name);
        }
    }

    private sealed record CheckOutcome(string InvariantId, IReadOnlyList<InvariantViolation> Violations, string? Error);
}
