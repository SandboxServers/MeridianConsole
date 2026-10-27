using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Contracts.Tests.Nodes;

/// <summary>
/// Pins the renewal timing that the agent and Nodes both rely on (D-S212). These fail if someone
/// raises the agent's retry delay or cuts the grace window below the agent's recovery window.
/// </summary>
public sealed class AgentCertificateRenewalPolicyTests
{
    [Fact]
    public void AgentRetry_IsWellInsideDefaultGrace()
    {
        Assert.True(
            AgentCertificateRenewalPolicy.AgentRetryMinutes * 4 <= AgentCertificateRenewalPolicy.DefaultGraceMinutes,
            "The agent must get several renewal retries inside the default grace window.");
    }

    [Fact]
    public void DefaultGrace_IsWithinMax()
    {
        Assert.InRange(
            AgentCertificateRenewalPolicy.DefaultGraceMinutes,
            0,
            AgentCertificateRenewalPolicy.MaxGraceMinutes);
    }
}
