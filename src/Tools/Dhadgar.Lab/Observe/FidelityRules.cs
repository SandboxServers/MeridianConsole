namespace Dhadgar.Lab.Observe;

/// <summary>
/// The facts that decide a run's replay fidelity (C18).
/// </summary>
/// <param name="ConcurrentActors">Actors ran at the same time.</param>
/// <param name="TimingFaults">Timing toxics were active.</param>
/// <param name="Truncated">The run hit the observation cap.</param>
/// <param name="Attested">A human attested the result.</param>
/// <param name="BuildPresent">The build named in the fingerprint still exists.</param>
public sealed record FidelityInput(bool ConcurrentActors, bool TimingFaults, bool Truncated, bool Attested, bool BuildPresent);

/// <summary>
/// Chooses a run's <see cref="ReplayFidelity"/>. <c>EvidenceOnly</c> wins over the others; then <c>ScheduleApproximate</c>
/// for concurrency or timing faults; otherwise <c>Exact</c>.
/// </summary>
public static class FidelityRules
{
    /// <summary>
    /// The fidelity for the given facts.
    /// </summary>
    public static ReplayFidelity For(FidelityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Truncated || input.Attested || !input.BuildPresent)
        {
            return ReplayFidelity.EvidenceOnly;
        }

        if (input.ConcurrentActors || input.TimingFaults)
        {
            return ReplayFidelity.ScheduleApproximate;
        }

        return ReplayFidelity.Exact;
    }
}
