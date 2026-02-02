using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Platform-agnostic interface for certificate storage.
/// Windows implements using X509Store, Linux uses file-based storage.
/// </summary>
public interface ICertificateStore
{
    /// <summary>
    /// Gets the current client certificate for mTLS authentication.
    /// </summary>
    /// <returns>The client certificate, or null if not enrolled.</returns>
    X509Certificate2? GetClientCertificate();

    /// <summary>
    /// Stores a new client certificate received during enrollment or renewal.
    /// </summary>
    /// <param name="certificate">Certificate to store.</param>
    /// <param name="privateKey">Private key bytes (PEM or PFX format depending on platform).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreCertificateAsync(
        X509Certificate2 certificate,
        byte[] privateKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the current client certificate.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveCertificateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the CA certificate for validating the control plane.
    /// </summary>
    /// <returns>The CA certificate, or null if using system trust store.</returns>
    X509Certificate2? GetCaCertificate();

    /// <summary>
    /// Stores the CA certificate.
    /// </summary>
    /// <param name="certificate">CA certificate to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreCaCertificateAsync(
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the client certificate needs renewal.
    /// </summary>
    /// <param name="thresholdDays">Days before expiry to trigger renewal.</param>
    /// <returns>True if renewal is needed.</returns>
    bool NeedsRenewal(int thresholdDays);
}
