using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;

namespace Dhadgar.Nodes.Tests.Integration.AgentSeam;

/// <summary>
/// Builds client certificates from a self-signed CA that Nodes does not trust. The CA uses the same common name as
/// the real CA, so only the signature (not the name) distinguishes it. Public so that other test projects can reuse it.
/// </summary>
public static class ForeignCa
{
    /// <summary>
    /// Creates a client certificate for <paramref name="nodeId"/> issued by a foreign self-signed CA whose CN is
    /// <see cref="AgentCertificateProfile.CaCommonName"/>. The SPIFFE SAN, <c>CN={nodeId}</c> and the client-auth EKU
    /// are correct. The caller owns the returned certificate and must dispose it.
    /// </summary>
    public static X509Certificate2 CreateClientCertificate(Guid nodeId)
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);

        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caRequest = new CertificateRequest(
            new X500DistinguishedName($"CN={AgentCertificateProfile.CaCommonName}"),
            caKey,
            HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        using var caCertificate = caRequest.CreateSelfSigned(notBefore, notAfter);

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest(
            new X500DistinguishedName($"CN={nodeId:D}"),
            leafKey,
            HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.2") }, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri(AgentCertificateProfile.SpiffeId(nodeId)));
        leafRequest.CertificateExtensions.Add(san.Build());

        using var issued = leafRequest.Create(
            caCertificate,
            notBefore,
            notAfter,
            RandomNumberGenerator.GetBytes(16));

        return issued.CopyWithPrivateKey(leafKey);
    }
}
