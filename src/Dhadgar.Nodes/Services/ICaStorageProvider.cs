using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Nodes.Services;

/// <summary>
/// Abstraction for storing and retrieving CA certificate and private key.
/// Implementations can use Azure Key Vault (production) or local files (development).
/// </summary>
public interface ICaStorageProvider
{
    /// <summary>
    /// Checks if the CA certificate and key exist.
    /// </summary>
    Task<bool> ExistsAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores the CA certificate and private key.
    /// </summary>
    /// <param name="certificate">The CA certificate with private key.</param>
    Task StoreAsync(X509Certificate2 certificate, CancellationToken ct = default);

    /// <summary>
    /// Loads the CA certificate with private key.
    /// </summary>
    /// <returns>The CA certificate with private key.</returns>
    Task<X509Certificate2> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the CA certificate (public key only) in PEM format.
    /// </summary>
    Task<string> GetCertificatePemAsync(CancellationToken ct = default);
}
