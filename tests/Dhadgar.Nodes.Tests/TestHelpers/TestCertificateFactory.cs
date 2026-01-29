using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Nodes.Tests.TestHelpers;

/// <summary>
/// Provides helper methods for creating test certificates.
/// </summary>
public static class TestCertificateFactory
{
    /// <summary>
    /// Creates a basic self-signed certificate for testing.
    /// </summary>
    /// <returns>A self-signed X509Certificate2.</returns>
    public static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
