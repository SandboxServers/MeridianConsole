using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;

namespace Dhadgar.Nodes.Tests.Integration.AgentSeam;

/// <summary>
/// In-memory <see cref="ICertificateStore"/> for seam tests. Keeps the raw key bytes so tests can check
/// that the agent holds a key matching the issued certificate.
/// </summary>
public sealed class InMemoryCertificateStore : ICertificateStore
{
    private byte[]? _privateKey;

    /// <summary>The stored client certificate (no private key), or null.</summary>
    public X509Certificate2? StoredClientCertificate { get; private set; }

    /// <summary>The stored CA certificate, or null.</summary>
    public X509Certificate2? StoredCaCertificate { get; private set; }

    /// <summary>
    /// Returns the raw private key bytes the agent stored (PKCS#8 or SEC1 EC), or null. The array is the store's own
    /// copy: callers that keep it must copy it, and must not modify it.
    /// </summary>
    public byte[]? GetPrivateKey() => _privateKey;

    /// <summary>
    /// Returns a new certificate object carrying the stored private key. The caller owns it and must dispose it.
    /// </summary>
    public X509Certificate2? GetClientCertificate()
    {
        if (StoredClientCertificate is null || _privateKey is null)
        {
            return StoredClientCertificate is null
                ? null
                : X509CertificateLoader.LoadCertificate(StoredClientCertificate.RawData);
        }

        using var publicOnly = X509CertificateLoader.LoadCertificate(StoredClientCertificate.RawData);
        using var key = ECDsa.Create();
        try
        {
            // P10 switches the agent to PKCS#8; accept the legacy SEC1 form as well.
            try
            {
                key.ImportPkcs8PrivateKey(_privateKey, out _);
            }
            catch (CryptographicException)
            {
                key.ImportECPrivateKey(_privateKey, out _);
            }

            return publicOnly.CopyWithPrivateKey(key);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task StoreCertificateAsync(X509Certificate2 certificate, byte[] privateKey, CancellationToken cancellationToken = default)
    {
        StoredClientCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        _privateKey = (byte[])privateKey.Clone(); // caller zeroes its copy
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveCertificateAsync(CancellationToken cancellationToken = default)
    {
        StoredClientCertificate = null;
        _privateKey = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public X509Certificate2? GetCaCertificate() => StoredCaCertificate;

    /// <inheritdoc />
    public Task StoreCaCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        StoredCaCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool NeedsRenewal(int thresholdDays) => false;
}
