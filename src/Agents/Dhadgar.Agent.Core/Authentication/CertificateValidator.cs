using System.Security.Cryptography.X509Certificates;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Validates certificates for mTLS communication.
/// </summary>
public sealed class CertificateValidator
{
    private readonly X509Certificate2? _caCertificate;

    /// <summary>
    /// Timeout for online revocation checks to prevent hanging.
    /// </summary>
    private static readonly TimeSpan RevocationTimeout = TimeSpan.FromSeconds(5);

    public CertificateValidator(X509Certificate2? caCertificate = null)
    {
        _caCertificate = caCertificate;
    }

    /// <summary>
    /// Validate a server certificate.
    /// </summary>
    /// <param name="certificate">Certificate to validate.</param>
    /// <param name="chain">Certificate chain.</param>
    /// <returns>Validation result.</returns>
    public Result<bool> ValidateServerCertificate(
        X509Certificate2 certificate,
        X509Chain? chain)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        // Check expiration (normalize to UTC for correct comparison)
        var notAfterUtc = certificate.NotAfter.ToUniversalTime();
        var notBeforeUtc = certificate.NotBefore.ToUniversalTime();

        if (notAfterUtc < DateTime.UtcNow)
        {
            return Result<bool>.Failure(
                $"[Certificate.Expired] Certificate expired on {notAfterUtc:O}");
        }

        if (notBeforeUtc > DateTime.UtcNow)
        {
            return Result<bool>.Failure(
                $"[Certificate.NotYetValid] Certificate not valid until {notBeforeUtc:O}");
        }

        // Chain validation is required for mTLS security
        if (chain is null)
        {
            return Result<bool>.Failure(
                "[Certificate.ChainMissing] Certificate chain is required for validation");
        }

        // Configure revocation checking with bounded timeout to prevent hangs
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.UrlRetrievalTimeout = RevocationTimeout;

        // If we have a CA certificate, validate against custom trust; otherwise use system trust
        if (_caCertificate is not null)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(_caCertificate);
        }

        try
        {
            if (!chain.Build(certificate))
            {
                var errors = string.Join(", ",
                    chain.ChainStatus.Select(s => s.StatusInformation));
                return Result<bool>.Failure(
                    $"[Certificate.ChainValidationFailed] Chain validation failed: {errors}");
            }

            return Result<bool>.Success(true);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            var chainErrors = chain.ChainStatus.Length > 0
                ? string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation))
                : "none";
            return Result<bool>.Failure(
                $"[Certificate.ChainBuildError] Chain build failed: {ex.Message}. Chain status: {chainErrors}");
        }
    }

    /// <summary>
    /// Check if a certificate is nearing expiration.
    /// </summary>
    /// <param name="certificate">Certificate to check.</param>
    /// <param name="thresholdDays">Days before expiry.</param>
    /// <returns>True if within threshold.</returns>
    public static bool IsNearingExpiration(X509Certificate2 certificate, int thresholdDays)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentOutOfRangeException.ThrowIfNegative(thresholdDays);
        // Normalize to UTC for correct comparison
        return certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddDays(thresholdDays);
    }

    /// <summary>
    /// Extract the node ID from a certificate's subject.
    /// Expects format: CN=node-{guid}
    /// </summary>
    /// <param name="certificate">Certificate to extract from.</param>
    /// <returns>Node ID if found.</returns>
    public static Guid? ExtractNodeId(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        // Use GetNameInfo to properly parse the CN, handling edge cases like
        // escaped characters, multi-value RDNs, and non-standard encodings
        var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

        if (string.IsNullOrEmpty(commonName))
        {
            return null;
        }

        const string prefix = "node-";
        if (!commonName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var guidPart = commonName[prefix.Length..];
        return Guid.TryParse(guidPart, out var nodeId) ? nodeId : null;
    }
}
