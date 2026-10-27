using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Services;
using Xunit;

namespace Dhadgar.Nodes.Tests.Services;

/// <summary>
/// Pins the single usable-certificate rule (D-S212) and the hard-revoke helper that clears grace.
/// </summary>
public sealed class AgentCertificateStateTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private static AgentCertificate CreateCertificate() => new()
    {
        Id = Guid.NewGuid(),
        NodeId = Guid.NewGuid(),
        Thumbprint = "abc",
        SerialNumber = "01",
        NotBefore = Now.AddDays(-1),
        NotAfter = Now.AddDays(89),
        IssuedAt = Now.AddDays(-1),
    };

    [Fact]
    public void IsUsable_ActiveCertificate_IsUsable()
    {
        var certificate = CreateCertificate();

        Assert.True(AgentCertificateState.IsUsable(certificate, Now));
    }

    [Fact]
    public void IsUsable_RevokedWithoutGrace_IsNotUsable()
    {
        var certificate = CreateCertificate();
        certificate.IsRevoked = true;
        certificate.RevokedAt = Now.AddMinutes(-1);

        Assert.False(AgentCertificateState.IsUsable(certificate, Now));
    }

    [Fact]
    public void IsUsable_RevokedWithGraceInFuture_IsUsable()
    {
        var certificate = CreateCertificate();
        certificate.IsRevoked = true;
        certificate.RevokedAt = Now.AddMinutes(-1);
        certificate.GraceUntil = Now.AddMinutes(30);

        Assert.True(AgentCertificateState.IsUsable(certificate, Now));
    }

    [Fact]
    public void IsUsable_AtExactlyGraceUntil_IsNotUsable()
    {
        var certificate = CreateCertificate();
        certificate.IsRevoked = true;
        certificate.RevokedAt = Now.AddMinutes(-60);
        certificate.GraceUntil = Now;

        Assert.False(AgentCertificateState.IsUsable(certificate, Now));
    }

    [Fact]
    public void RevokeWithoutGrace_ClearsGrace_AndKeepsFirstRevokedAt()
    {
        var certificate = CreateCertificate();
        certificate.IsRevoked = true;
        certificate.RevokedAt = Now.AddMinutes(-10);
        certificate.RevocationReason = "Renewed - superseded by new certificate";
        certificate.GraceUntil = Now.AddMinutes(50);

        AgentCertificateState.RevokeWithoutGrace(certificate, Now, "Node decommissioned");

        Assert.True(certificate.IsRevoked);
        Assert.Null(certificate.GraceUntil);
        Assert.Equal(Now.AddMinutes(-10), certificate.RevokedAt);
        Assert.Equal("Renewed - superseded by new certificate", certificate.RevocationReason);
        Assert.False(AgentCertificateState.IsUsable(certificate, Now));
    }

    [Fact]
    public void RevokeWithoutGrace_ActiveCertificate_IsRevokedWithReason()
    {
        var certificate = CreateCertificate();

        AgentCertificateState.RevokeWithoutGrace(certificate, Now, "Node decommissioned");

        Assert.True(certificate.IsRevoked);
        Assert.Equal(Now, certificate.RevokedAt);
        Assert.Equal("Node decommissioned", certificate.RevocationReason);
        Assert.Null(certificate.GraceUntil);
    }
}
