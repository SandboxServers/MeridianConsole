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
    /// <summary>The stored client certificate (no private key), or null.</summary>
    public X509Certificate2? ClientCertificate { get; private set; }

    /// <summary>The raw private key bytes the agent stored (PKCS#8 or SEC1 EC), or null.</summary>
    public byte[]? PrivateKey { get; private set; }

    /// <summary>The stored CA certificate, or null.</summary>
    public X509Certificate2? CaCertificate { get; private set; }

    /// <summary>
    /// Returns a new certificate object carrying the stored private key. The caller owns it and must dispose it.
    /// </summary>
    public X509Certificate2? GetClientCertificate()
    {
        if (ClientCertificate is null || PrivateKey is null)
        {
            return ClientCertificate is null
                ? null
                : X509CertificateLoader.LoadCertificate(ClientCertificate.RawData);
        }

        using var publicOnly = X509CertificateLoader.LoadCertificate(ClientCertificate.RawData);
        using var key = ECDsa.Create();
        try
        {
            // P10 switches the agent to PKCS#8; accept the legacy SEC1 form as well.
            try
            {
                key.ImportPkcs8PrivateKey(PrivateKey, out _);
            }
            catch (CryptographicException)
            {
                key.ImportECPrivateKey(PrivateKey, out _);
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
        ClientCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        PrivateKey = (byte[])privateKey.Clone(); // caller zeroes its copy
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveCertificateAsync(CancellationToken cancellationToken = default)
    {
        ClientCertificate = null;
        PrivateKey = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public X509Certificate2? GetCaCertificate() => CaCertificate;

    /// <inheritdoc />
    public Task StoreCaCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        CaCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool NeedsRenewal(int thresholdDays) => false;
}
