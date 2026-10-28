using Dhadgar.Lab.Observe;

namespace Dhadgar.Lab.Tests.Observe;

/// <summary>
/// Each row of the C18 fidelity rule, and the precedence between them.
/// </summary>
public sealed class FidelityRulesTests
{
    [Fact]
    public void PlainRun_IsExact()
    {
        var input = new FidelityInput(ConcurrentActors: false, TimingFaults: false, Truncated: false, Attested: false, BuildPresent: true);

        Assert.Equal(ReplayFidelity.Exact, FidelityRules.For(input));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ConcurrencyOrTimingFaults_IsScheduleApproximate(bool concurrent, bool timing)
    {
        var input = new FidelityInput(concurrent, timing, Truncated: false, Attested: false, BuildPresent: true);

        Assert.Equal(ReplayFidelity.ScheduleApproximate, FidelityRules.For(input));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void TruncatedAttestedOrNoBuild_IsEvidenceOnly(bool truncated, bool attested, bool noBuild)
    {
        var input = new FidelityInput(ConcurrentActors: false, TimingFaults: false, truncated, attested, BuildPresent: !noBuild);

        Assert.Equal(ReplayFidelity.EvidenceOnly, FidelityRules.For(input));
    }

    [Fact]
    public void EvidenceOnly_WinsOverScheduleApproximate()
    {
        var input = new FidelityInput(ConcurrentActors: true, TimingFaults: true, Truncated: true, Attested: false, BuildPresent: true);

        Assert.Equal(ReplayFidelity.EvidenceOnly, FidelityRules.For(input));
    }
}
