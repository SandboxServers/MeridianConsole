using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Nodes.Services;

/// <summary>
/// Service for managing the Certificate Authority and issuing client certificates for agents.
/// </summary>
public interface ICertificateAuthorityService
{
    /// <summary>
    /// Ensures the CA is initialized. Called at startup.
    /// Creates the CA certificate and key if they don't exist.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Issues a new client certificate for an agent.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the certificate details and PKCS#12 bundle.</returns>
    Task<CertificateIssuanceResult> IssueCertificateAsync(
        Guid nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Renews an existing certificate for an agent.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node.</param>
    /// <param name="currentCertificateThumbprint">The thumbprint of the current certificate to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the new certificate details and PKCS#12 bundle.</returns>
    Task<CertificateIssuanceResult> RenewCertificateAsync(
        Guid nodeId,
        string currentCertificateThumbprint,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the CA certificate in PEM format for agents to add to their trust store.
    /// </summary>
    Task<string> GetCaCertificatePemAsync(CancellationToken ct = default);

    /// <summary>
    /// Validates a certificate against the CA.
    /// </summary>
    /// <param name="certificatePem">The certificate in PEM format.</param>
    /// <returns>True if the certificate is valid and signed by this CA.</returns>
    Task<bool> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default);
}

/// <summary>
/// Result of a certificate issuance operation.
/// </summary>
public sealed record CertificateIssuanceResult
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// SHA-256 thumbprint of the certificate.
    /// </summary>
    public string? Thumbprint { get; init; }

    /// <summary>
    /// Certificate serial number.
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Certificate validity start date.
    /// </summary>
    public DateTime? NotBefore { get; init; }

    /// <summary>
    /// Certificate expiration date.
    /// </summary>
    public DateTime? NotAfter { get; init; }

    /// <summary>
    /// The client certificate in PEM format.
    /// </summary>
    public string? CertificatePem { get; init; }

    /// <summary>
    /// The PKCS#12 bundle containing certificate and private key.
    /// Base64 encoded.
    /// </summary>
    public string? Pkcs12Base64 { get; init; }

    /// <summary>
    /// Password for the PKCS#12 bundle.
    /// </summary>
    public string? Pkcs12Password { get; init; }

    public static CertificateIssuanceResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };

    public static CertificateIssuanceResult Ok(
        string thumbprint,
        string serialNumber,
        DateTime notBefore,
        DateTime notAfter,
        string certificatePem,
        string pkcs12Base64,
        string pkcs12Password) => new()
    {
        Success = true,
        Thumbprint = thumbprint,
        SerialNumber = serialNumber,
        NotBefore = notBefore,
        NotAfter = notAfter,
        CertificatePem = certificatePem,
        Pkcs12Base64 = pkcs12Base64,
        Pkcs12Password = pkcs12Password
    };
}
