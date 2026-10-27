using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Nodes.Services;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Test implementation of ICertificateAuthorityService that generates fake certificates
/// for use in unit tests without actual cryptographic operations.
/// </summary>
public sealed class TestCertificateAuthorityService : ICertificateAuthorityService
{
    private readonly TimeProvider _timeProvider;
    private readonly int _certificateValidityDays;
    private bool _isInitialized;
    private readonly List<(Guid NodeId, string Thumbprint)> _issuedCertificates = [];

    public TestCertificateAuthorityService(
        TimeProvider timeProvider,
        int certificateValidityDays = 90)
    {
        _timeProvider = timeProvider;
        _certificateValidityDays = certificateValidityDays;
    }

    public IReadOnlyList<(Guid NodeId, string Thumbprint)> IssuedCertificates => _issuedCertificates;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        _isInitialized = true;
        return Task.CompletedTask;
    }

    public Task<CertificateIssuanceResult> IssueCertificateAsync(Guid nodeId, CancellationToken ct = default)
    {
        if (!_isInitialized)
        {
            return Task.FromResult(CertificateIssuanceResult.Fail("CA not initialized"));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Generate fake but consistent thumbprint and serial
        var thumbprintBytes = SHA256.HashData(nodeId.ToByteArray().Concat(BitConverter.GetBytes(now.Ticks)).ToArray());
        var thumbprint = Convert.ToHexString(thumbprintBytes).ToLowerInvariant();

        // Deterministic serial from nodeId for test reproducibility
        var serialBytes = SHA256.HashData(
            nodeId.ToByteArray()
                .Concat(System.Text.Encoding.UTF8.GetBytes(now.ToString("O")))
                .ToArray()
        ).Take(16).ToArray();
        var serialNumber = Convert.ToHexString(serialBytes).ToLowerInvariant();

        // Deterministic password from nodeId for test reproducibility
        var password = Convert.ToBase64String(
            SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"test-password-{nodeId}")
            )
        );

        // Generate a fake PEM certificate format
        var fakeCertData = Convert.ToBase64String(thumbprintBytes);
        var certPem = $"-----BEGIN CERTIFICATE-----\n{fakeCertData}\n-----END CERTIFICATE-----";

        // Generate fake PKCS#12 data
        var pkcs12Data = Convert.ToBase64String(thumbprintBytes.Concat(serialBytes).ToArray());

        _issuedCertificates.Add((nodeId, thumbprint));

        return Task.FromResult(CertificateIssuanceResult.Ok(
            thumbprint,
            serialNumber,
            now,
            now.AddDays(_certificateValidityDays),
            certPem,
            pkcs12Data,
            password));
    }

    public Task<CertificateIssuanceResult> IssueCertificateForPublicKeyAsync(
        Guid nodeId,
        string subjectPublicKeyInfoBase64,
        CancellationToken ct = default)
    {
        if (!_isInitialized)
        {
            return Task.FromResult(CertificateIssuanceResult.Fail("CA not initialized"));
        }

        PublicKey publicKey;
        try
        {
            publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(
                Convert.FromBase64String(subjectPublicKeyInfoBase64), out _);
            using var rsa = publicKey.GetRSAPublicKey();
            if (rsa is not null && rsa.KeySize < 2048)
            {
                return Task.FromResult(CertificateIssuanceResult.Fail("invalid_public_key"));
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return Task.FromResult(CertificateIssuanceResult.Fail("invalid_public_key"));
        }

        X509Certificate2? leaf = null;
        try
        {
            // Real signing against an ephemeral CA, so the issued certificate is bound to the request key.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            using var caKey = RSA.Create(2048);
            var caRequest = new CertificateRequest("CN=Test Agent CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using var caCert = caRequest.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));

            var leafRequest = new CertificateRequest(
                new X500DistinguishedName($"CN={nodeId}"), publicKey, HashAlgorithmName.SHA256);
            leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], true));

            var serialBytes = RandomNumberGenerator.GetBytes(16);
            serialBytes[0] &= 0x7F;
            var signer = X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1);
            leaf = leafRequest.Create(caCert.SubjectName, signer, now, now.AddDays(_certificateValidityDays), serialBytes);

            var thumbprint = Convert.ToHexString(SHA256.HashData(leaf.RawData)).ToLowerInvariant();
            var serialNumber = Convert.ToHexString(leaf.GetSerialNumber().Reverse().ToArray()).ToLowerInvariant();
            return Task.FromResult(CertificateIssuanceResult.Ok(
                thumbprint,
                serialNumber,
                now,
                now.AddDays(_certificateValidityDays),
                leaf.ExportCertificatePem()));
        }
        finally
        {
            leaf?.Dispose();
        }
    }

    public Task<string> GetCaCertificatePemAsync(CancellationToken ct = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("CA not initialized");
        }

        return Task.FromResult(
            "-----BEGIN CERTIFICATE-----\n" +
            "MIIB+TCCAaGgAwIBAgIUFakeCA==\n" +
            "-----END CERTIFICATE-----");
    }

    public Task<CertificateTrustStatus> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default)
    {
        // In tests, we consider a certificate valid if it contains our fake header
        return Task.FromResult(certificatePem.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal)
            ? CertificateTrustStatus.Trusted
            : CertificateTrustStatus.Untrusted);
    }
}
