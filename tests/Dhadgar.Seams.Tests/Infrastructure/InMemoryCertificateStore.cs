using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;

namespace Dhadgar.Seams.Tests.Infrastructure;

/// <summary>
/// In-memory <see cref="ICertificateStore"/> for a seam agent. Holds the client certificate and CA the agent receives.
/// </summary>
internal sealed class InMemoryCertificateStore : ICertificateStore
{
    public X509Certificate2? ClientCertificate { get; private set; }

    public byte[]? PrivateKey { get; private set; }

    public X509Certificate2? CaCertificate { get; private set; }

    /// <summary>Returns a fresh copy; the caller disposes it.</summary>
    public X509Certificate2? GetClientCertificate() => ClientCertificate is null
        ? null
        : X509CertificateLoader.LoadCertificate(ClientCertificate.RawData);

    public Task StoreCertificateAsync(X509Certificate2 certificate, byte[] privateKey, CancellationToken cancellationToken = default)
    {
        ClientCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        PrivateKey = (byte[])privateKey.Clone(); // caller zeroes its copy
        return Task.CompletedTask;
    }

    public Task RemoveCertificateAsync(CancellationToken cancellationToken = default)
    {
        ClientCertificate = null;
        PrivateKey = null;
        return Task.CompletedTask;
    }

    public X509Certificate2? GetCaCertificate() => CaCertificate;

    public Task StoreCaCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        CaCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return Task.CompletedTask;
    }

    public bool NeedsRenewal(int thresholdDays) => false;
}
