using System.Security.Cryptography.X509Certificates;
using Dhadgar.Shared.Results;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Validates certificates for mTLS communication.
/// </summary>
public sealed class CertificateValidator
{
    private readonly X509Certificate2? _caCertificate;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Timeout for online revocation checks to prevent hanging.
    /// </summary>
    private static readonly TimeSpan RevocationTimeout = TimeSpan.FromSeconds(5);

    public CertificateValidator(X509Certificate2? caCertificate = null, TimeProvider? timeProvider = null)
    {
        _caCertificate = caCertificate;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Validate a server certificate.
    /// </summary>
    /// <param name="certificate">Certificate to validate.</param>
    /// <param name="chain">Certificate chain.</param>
    /// <returns>Validation result.</returns>
    public Result<bool> ValidateServerCertificate(
        X509Certificate2? certificate,
        X509Chain? chain)
    {
        // Return Result failure instead of throwing for railway-oriented flow
        if (certificate is null)
        {
            return Result<bool>.Failure("[Certificate.Null] Certificate cannot be null");
        }

        // Check expiration (normalize to UTC for correct comparison)
        var notAfterUtc = certificate.NotAfter.ToUniversalTime();
        var notBeforeUtc = certificate.NotBefore.ToUniversalTime();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (notAfterUtc < utcNow)
        {
            return Result<bool>.Failure(
                $"[Certificate.Expired] Certificate expired on {notAfterUtc:O}");
        }

        if (notBeforeUtc > utcNow)
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
    /// <param name="timeProvider">Time provider for testability (defaults to system time).</param>
    /// <returns>Result containing true if within threshold, false otherwise, or failure for invalid inputs.</returns>
    public static Result<bool> IsNearingExpiration(X509Certificate2? certificate, int thresholdDays, TimeProvider? timeProvider = null)
    {
        if (certificate is null)
        {
            return Result<bool>.Failure("[Certificate.Null] Certificate cannot be null");
        }

        if (thresholdDays < 0)
        {
            return Result<bool>.Failure("[Certificate.InvalidThreshold] Threshold days cannot be negative");
        }

        // Normalize to UTC for correct comparison
        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var isNearing = certificate.NotAfter.ToUniversalTime() <= utcNow.AddDays(thresholdDays);
        return Result<bool>.Success(isNearing);
    }

    /// <summary>
    /// Extract the node ID from a certificate's subject.
    /// Expects format: CN=node-{guid}
    /// </summary>
    /// <param name="certificate">Certificate to extract from.</param>
    /// <returns>Result containing the node ID if found, null if not in expected format, or failure for errors.</returns>
    public static Result<Guid?> ExtractNodeId(X509Certificate2? certificate)
    {
        if (certificate is null)
        {
            return Result<Guid?>.Failure("[Certificate.Null] Certificate cannot be null");
        }

        // Use GetNameInfo to properly parse the CN, handling edge cases like
        // escaped characters, multi-value RDNs, and non-standard encodings
        var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

        if (string.IsNullOrEmpty(commonName))
        {
            return Result<Guid?>.Failure("[Certificate.MissingCN] Certificate has no common name");
        }

        const string prefix = "node-";
        if (!commonName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Result<Guid?>.Failure("[Certificate.InvalidCNFormat] Common name does not start with 'node-'");
        }

        var guidPart = commonName[prefix.Length..];
        if (!Guid.TryParse(guidPart, out var nodeId))
        {
            return Result<Guid?>.Failure("[Certificate.InvalidNodeId] Common name does not contain a valid GUID");
        }

        return Result<Guid?>.Success(nodeId);
    }
}
