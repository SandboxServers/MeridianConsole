using System.Security.Cryptography;
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

        var serialBytes = RandomNumberGenerator.GetBytes(16);
        var serialNumber = Convert.ToHexString(serialBytes).ToLowerInvariant();

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

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

    public Task<CertificateIssuanceResult> RenewCertificateAsync(
        Guid nodeId,
        string currentCertificateThumbprint,
        CancellationToken ct = default)
    {
        // For renewal, we just issue a new certificate
        return IssueCertificateAsync(nodeId, ct);
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

    public Task<bool> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default)
    {
        // In tests, we consider a certificate valid if it contains our fake header
        return Task.FromResult(certificatePem.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal));
    }
}
