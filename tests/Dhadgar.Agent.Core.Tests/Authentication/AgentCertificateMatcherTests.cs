using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Authentication;

/// <summary>
/// The Nodes-shaped certificate profile the Windows store matches on (D-S219, D-S235). Runs on Linux CI.
/// </summary>
public sealed class AgentCertificateMatcherTests : IDisposable
{
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly NodesStyleIssuer _nodes = new();
    private readonly List<X509Certificate2> _certificates = [];

    public void Dispose()
    {
        foreach (var certificate in _certificates)
        {
            certificate.Dispose();
        }

        _nodes.Dispose();
    }

    [Fact]
    public void NodesShapedCertificate_Matches()
    {
        var certificate = Track(_nodes.IssueLeaf(
            Guid.NewGuid(),
            notBefore: _now.AddMinutes(-1),
            notAfter: _now.AddDays(30)));

        Assert.True(AgentCertificateMatcher.IsAgentClientIdentity(certificate));
        Assert.True(AgentCertificateMatcher.IsAgentClientCertificate(certificate, _now));
    }

    [Fact]
    public void CommonNameFromAnotherIssuer_DoesNotMatch()
    {
        // Same CN the old store matched on, but issued by its own CA and carrying a SPIFFE SAN
        var certificate = Track(_nodes.IssueSelfSigned(
            "dhadgar-agent",
            Guid.NewGuid(),
            notBefore: _now.AddMinutes(-1),
            notAfter: _now.AddDays(30)));

        Assert.False(AgentCertificateMatcher.IsAgentClientIdentity(certificate));
        Assert.False(AgentCertificateMatcher.IsAgentClientCertificate(certificate, _now));
    }

    [Fact]
    public void ExpiredCertificate_IsNotAnAgentClientCertificate_ButIsStillAnAgentIdentity()
    {
        var certificate = Track(_nodes.IssueLeaf(
            Guid.NewGuid(),
            notBefore: _now.AddDays(-2),
            notAfter: _now.AddDays(-1)));

        Assert.False(AgentCertificateMatcher.IsAgentClientCertificate(certificate, _now));

        // Identity ignores time, so the store can still remove an expired copy of the agent certificate
        Assert.True(AgentCertificateMatcher.IsAgentClientIdentity(certificate));
    }

    [Fact]
    public void CertificateWithoutSpiffeSan_DoesNotMatch()
    {
        var certificate = Track(_nodes.IssueLeaf(
            spiffeId: null,
            notBefore: _now.AddMinutes(-1),
            notAfter: _now.AddDays(30)));

        Assert.False(AgentCertificateMatcher.IsAgentClientIdentity(certificate));
        Assert.False(AgentCertificateMatcher.IsAgentClientCertificate(certificate, _now));
    }

    [Fact]
    public void CertificateWithSpiffeSanOutsideNodesPrefix_DoesNotMatch()
    {
        var certificate = Track(_nodes.IssueLeaf(
            spiffeId: "spiffe://example.test/workloads/other",
            notBefore: _now.AddMinutes(-1),
            notAfter: _now.AddDays(30)));

        Assert.False(AgentCertificateMatcher.IsAgentClientIdentity(certificate));
    }

    [Fact]
    public void CertificateNotBeforeThreeMinutesAhead_Matches_WithinClockSkew()
    {
        // A Nodes clock running ahead stamps NotBefore slightly in the future (D-S235)
        var certificate = Track(_nodes.IssueLeaf(
            Guid.NewGuid(),
            notBefore: _now.AddMinutes(3),
            notAfter: _now.AddDays(30)));

        Assert.True(AgentCertificateMatcher.IsAgentClientCertificate(certificate, _now));
    }

    [Fact]
    public void CertificateNotBeforeSixMinutesAhead_DoesNotMatch()
    {
        var certificate = Track(_nodes.IssueLeaf(
            Guid.NewGuid(),
            notBefore: _now.AddMinutes(6),
            notAfter: _now.AddDays(30)));

        Assert.False(AgentCertificateMatcher.IsAgentClientCertificate(certificate, _now));
    }

    [Fact]
    public void CaCertificate_IsAgentCa_AndLeafIsNot()
    {
        var ca = Track(_nodes.CopyCa());
        var leaf = Track(_nodes.IssueLeaf(
            Guid.NewGuid(),
            notBefore: _now.AddMinutes(-1),
            notAfter: _now.AddDays(30)));

        Assert.True(AgentCertificateMatcher.IsAgentCa(ca));
        Assert.False(AgentCertificateMatcher.IsAgentCa(leaf));
    }

    private X509Certificate2 Track(X509Certificate2 certificate)
    {
        _certificates.Add(certificate);
        return certificate;
    }

    /// <summary>
    /// Issues certificates shaped like Nodes' agent CA output: the CA is named by
    /// <see cref="AgentCertificateProfile.CaCommonName"/>, leaves are CN={nodeId}, O=MeridianConsole with a SPIFFE URI SAN.
    /// </summary>
    [Fact]
    public void MalformedSanAfterSpiffeUri_IsNotAnAgentIdentity()
    {
        // A valid Nodes SPIFFE URI GeneralName (context [6]), then a second [6] element whose declared length
        // runs past the end of the extension. A partial read must not classify the certificate as an agent.
        var uri = Encoding.ASCII.GetBytes(AgentCertificateProfile.SpiffeId(Guid.NewGuid()));
        var validElement = new byte[] { 0x86, (byte)uri.Length }.Concat(uri).ToArray();
        var truncatedElement = new byte[] { 0x86, 0x20 };
        var sanContent = validElement.Concat(truncatedElement).ToArray();
        var sanExtension = new byte[] { 0x30, (byte)sanContent.Length }.Concat(sanContent).ToArray();

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={AgentCertificateProfile.CaCommonName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509Extension("2.5.29.17", sanExtension, critical: false));
        using var selfSigned = request.CreateSelfSigned(_now.AddMinutes(-1), _now.AddDays(30));
        var certificate = Track(X509CertificateLoader.LoadCertificate(selfSigned.RawData));

        Assert.False(AgentCertificateMatcher.IsAgentClientIdentity(certificate));
        Assert.False(AgentCertificateMatcher.IsAgentClientCertificate(certificate, _now));
    }
    private sealed class NodesStyleIssuer : IDisposable
    {
        private readonly ECDsa _caKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        private readonly X509Certificate2 _ca;

        public NodesStyleIssuer()
        {
            var request = new CertificateRequest(
                new X500DistinguishedName($"CN={AgentCertificateProfile.CaCommonName}"),
                _caKey,
                HashAlgorithmName.SHA384);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

            _ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        }

        /// <summary>A fresh copy of the CA certificate that the caller owns.</summary>
        public X509Certificate2 CopyCa() => X509CertificateLoader.LoadCertificate(_ca.RawData);

        /// <summary>Issues a leaf with the Nodes subject and the Nodes SPIFFE SAN for <paramref name="nodeId"/>.</summary>
        public X509Certificate2 IssueLeaf(Guid nodeId, DateTimeOffset notBefore, DateTimeOffset notAfter) =>
            IssueLeaf(AgentCertificateProfile.SpiffeId(nodeId), notBefore, notAfter);

        /// <summary>Issues a leaf with the given SPIFFE URI, or none when <paramref name="spiffeId"/> is null.</summary>
        public X509Certificate2 IssueLeaf(string? spiffeId, DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                new X500DistinguishedName($"CN={Guid.NewGuid():D}, O={AgentCertificateProfile.Organization}"),
                leafKey,
                HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

            if (spiffeId is not null)
            {
                var san = new SubjectAlternativeNameBuilder();
                san.AddUri(new Uri(spiffeId));
                request.CertificateExtensions.Add(san.Build());
            }

            return Sign(request, _ca.SubjectName, notBefore, notAfter);
        }

        /// <summary>Issues a self-signed certificate under <paramref name="commonName"/> with a Nodes-style SPIFFE SAN.</summary>
        public X509Certificate2 IssueSelfSigned(string commonName, Guid nodeId, DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                new X500DistinguishedName($"CN={commonName}"),
                otherKey,
                HashAlgorithmName.SHA256);
            var san = new SubjectAlternativeNameBuilder();
            san.AddUri(new Uri(AgentCertificateProfile.SpiffeId(nodeId)));
            request.CertificateExtensions.Add(san.Build());

            using var issued = request.CreateSelfSigned(notBefore, notAfter);
            return X509CertificateLoader.LoadCertificate(issued.RawData);
        }

        private X509Certificate2 Sign(
            CertificateRequest request,
            X500DistinguishedName issuerName,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter)
        {
            var generator = X509SignatureGenerator.CreateForECDsa(_caKey);
            using var issued = request.Create(
                issuerName,
                generator,
                notBefore,
                notAfter,
                RandomNumberGenerator.GetBytes(16));

            return X509CertificateLoader.LoadCertificate(issued.RawData);
        }

        public void Dispose()
        {
            _ca.Dispose();
            _caKey.Dispose();
        }
    }
}
