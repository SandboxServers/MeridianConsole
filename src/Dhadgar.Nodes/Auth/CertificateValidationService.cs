using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Service for validating client certificates and extracting identity information.
/// </summary>
public interface ICertificateValidationService
{
    /// <summary>
    /// Validates a client certificate against the CA and checks for required properties.
    /// </summary>
    /// <param name="certificate">The client certificate to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with details about the certificate.</returns>
    Task<CertificateValidationResult> ValidateClientCertificateAsync(
        X509Certificate2 certificate,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts the SPIFFE ID from a certificate's Subject Alternative Name (SAN) extension.
    /// </summary>
    /// <param name="certificate">The certificate to extract from.</param>
    /// <returns>The SPIFFE ID if found, null otherwise.</returns>
    string? ExtractSpiffeId(X509Certificate2 certificate);

    /// <summary>
    /// Parses the node ID from a SPIFFE ID.
    /// </summary>
    /// <param name="spiffeId">The SPIFFE ID (e.g., "spiffe://meridianconsole.com/nodes/{nodeId}").</param>
    /// <returns>The parsed node ID if valid, null otherwise.</returns>
    Guid? ParseNodeIdFromSpiffeId(string spiffeId);
}

/// <summary>
/// Result of a certificate validation operation.
/// </summary>
public sealed record CertificateValidationResult
{
    /// <summary>
    /// Whether the certificate is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// The extracted node ID if the certificate is valid.
    /// </summary>
    public Guid? NodeId { get; init; }

    /// <summary>
    /// The SPIFFE ID from the certificate.
    /// </summary>
    public string? SpiffeId { get; init; }

    /// <summary>
    /// Certificate thumbprint (SHA-256).
    /// </summary>
    public string? Thumbprint { get; init; }

    /// <summary>
    /// Error code if validation failed.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Detailed error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether the certificate is expired (for diagnostic purposes).
    /// </summary>
    public bool IsExpired { get; init; }

    /// <summary>
    /// Certificate expiration date.
    /// </summary>
    public DateTime? NotAfter { get; init; }

    public static CertificateValidationResult Success(
        Guid nodeId,
        string spiffeId,
        string thumbprint,
        DateTime notAfter) => new()
    {
        IsValid = true,
        NodeId = nodeId,
        SpiffeId = spiffeId,
        Thumbprint = thumbprint,
        NotAfter = notAfter
    };

    public static CertificateValidationResult Fail(
        string errorCode,
        string errorMessage,
        bool isExpired = false,
        DateTime? notAfter = null) => new()
    {
        IsValid = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        IsExpired = isExpired,
        NotAfter = notAfter
    };
}

/// <summary>
/// Implementation of certificate validation that integrates with the CA service.
/// </summary>
public sealed partial class CertificateValidationService : ICertificateValidationService
{
    private readonly ICertificateAuthorityService _caService;
    private readonly MtlsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CertificateValidationService> _logger;

    // SPIFFE ID pattern: spiffe://meridianconsole.com/nodes/{nodeId}
    [GeneratedRegex(@"^spiffe://(?<domain>[^/]+)/nodes/(?<nodeId>[a-fA-F0-9\-]{36})$")]
    private static partial Regex SpiffeIdPattern();

    // OID for Subject Alternative Name extension
    private const string SubjectAlternativeNameOid = "2.5.29.17";

    // OID for Client Authentication
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    public CertificateValidationService(
        ICertificateAuthorityService caService,
        IOptions<MtlsOptions> options,
        TimeProvider timeProvider,
        ILogger<CertificateValidationService> logger)
    {
        _caService = caService;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CertificateValidationResult> ValidateClientCertificateAsync(
        X509Certificate2 certificate,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Check certificate validity period
        var isExpired = certificate.NotAfter < now;
        var notYetValid = certificate.NotBefore > now;

        if (notYetValid)
        {
            _logger.LogWarning(
                "Certificate not yet valid. NotBefore: {NotBefore}, Current time: {Now}",
                certificate.NotBefore, now);

            return CertificateValidationResult.Fail(
                "certificate_not_yet_valid",
                $"Certificate is not yet valid. NotBefore: {certificate.NotBefore:O}",
                notAfter: certificate.NotAfter);
        }

        if (isExpired && !_options.AllowExpiredCertificates)
        {
            _logger.LogWarning(
                "Certificate expired. NotAfter: {NotAfter}, Current time: {Now}",
                certificate.NotAfter, now);

            return CertificateValidationResult.Fail(
                "certificate_expired",
                $"Certificate expired at {certificate.NotAfter:O}",
                isExpired: true,
                notAfter: certificate.NotAfter);
        }

        if (isExpired && _options.AllowExpiredCertificates)
        {
            _logger.LogWarning(
                "Allowing expired certificate (AllowExpiredCertificates=true). NotAfter: {NotAfter}",
                certificate.NotAfter);
        }

        // Validate certificate was signed by our CA
        var certPem = certificate.ExportCertificatePem();
        var isSignedByOurCa = await _caService.ValidateCertificateAsync(certPem, ct);

        if (!isSignedByOurCa)
        {
            _logger.LogWarning(
                "Certificate not signed by our CA. Subject: {Subject}, Thumbprint: {Thumbprint}",
                certificate.Subject,
                certificate.GetCertHashString());

            return CertificateValidationResult.Fail(
                "invalid_issuer",
                "Certificate was not issued by the Meridian Console CA",
                notAfter: certificate.NotAfter);
        }

        // Check for client authentication EKU
        if (!HasClientAuthenticationEku(certificate))
        {
            _logger.LogWarning(
                "Certificate missing Client Authentication EKU. Subject: {Subject}",
                certificate.Subject);

            return CertificateValidationResult.Fail(
                "missing_client_auth_eku",
                "Certificate does not have Client Authentication Extended Key Usage",
                notAfter: certificate.NotAfter);
        }

        // Extract and validate SPIFFE ID
        var spiffeId = ExtractSpiffeId(certificate);
        if (string.IsNullOrEmpty(spiffeId))
        {
            _logger.LogWarning(
                "Certificate missing SPIFFE ID in SAN. Subject: {Subject}",
                certificate.Subject);

            return CertificateValidationResult.Fail(
                "missing_spiffe_id",
                "Certificate does not contain a SPIFFE ID in Subject Alternative Name",
                notAfter: certificate.NotAfter);
        }

        // Parse and validate node ID from SPIFFE ID
        var nodeId = ParseNodeIdFromSpiffeId(spiffeId);
        if (!nodeId.HasValue)
        {
            _logger.LogWarning(
                "Invalid SPIFFE ID format. SpiffeId: {SpiffeId}, Subject: {Subject}",
                spiffeId, certificate.Subject);

            return CertificateValidationResult.Fail(
                "invalid_spiffe_id",
                $"SPIFFE ID format is invalid: {spiffeId}",
                notAfter: certificate.NotAfter);
        }

        // Validate trust domain
        var match = SpiffeIdPattern().Match(spiffeId);
        if (match.Success)
        {
            var domain = match.Groups["domain"].Value;
            if (!string.Equals(domain, _options.SpiffeTrustDomain, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "SPIFFE ID trust domain mismatch. Expected: {Expected}, Actual: {Actual}",
                    _options.SpiffeTrustDomain, domain);

                return CertificateValidationResult.Fail(
                    "trust_domain_mismatch",
                    $"Certificate SPIFFE ID has wrong trust domain. Expected: {_options.SpiffeTrustDomain}, Actual: {domain}",
                    notAfter: certificate.NotAfter);
            }
        }

        // Calculate SHA-256 thumbprint
        var thumbprint = CalculateSha256Thumbprint(certificate);

        _logger.LogDebug(
            "Certificate validated successfully. NodeId: {NodeId}, SpiffeId: {SpiffeId}, Thumbprint: {Thumbprint}",
            nodeId.Value, spiffeId, thumbprint);

        return CertificateValidationResult.Success(
            nodeId.Value,
            spiffeId,
            thumbprint,
            certificate.NotAfter);
    }

    public string? ExtractSpiffeId(X509Certificate2 certificate)
    {
        // Find the Subject Alternative Name extension
        var sanExtension = certificate.Extensions
            .OfType<X509Extension>()
            .FirstOrDefault(e => e.Oid?.Value == SubjectAlternativeNameOid);

        if (sanExtension is null)
        {
            return null;
        }

        // Try to parse using the built-in SAN extension class
        // The SAN extension contains URIs as uniformResourceIdentifier (OID 6)
        try
        {
            var formattedString = sanExtension.Format(multiLine: true);

            // Look for URI entries in the formatted output
            // Format is typically "URL=spiffe://..." or "URI:spiffe://..."
            var lines = formattedString.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Handle different format strings from .NET
                if (trimmedLine.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = trimmedLine["URL=".Length..];
                    if (uri.StartsWith("spiffe://", StringComparison.OrdinalIgnoreCase))
                    {
                        return uri;
                    }
                }
                else if (trimmedLine.StartsWith("URI:", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = trimmedLine["URI:".Length..];
                    if (uri.StartsWith("spiffe://", StringComparison.OrdinalIgnoreCase))
                    {
                        return uri;
                    }
                }
                else if (trimmedLine.StartsWith("Uniform Resource Identifier=", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = trimmedLine["Uniform Resource Identifier=".Length..];
                    if (uri.StartsWith("spiffe://", StringComparison.OrdinalIgnoreCase))
                    {
                        return uri;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse SAN extension");
        }

        return null;
    }

    public Guid? ParseNodeIdFromSpiffeId(string spiffeId)
    {
        if (string.IsNullOrEmpty(spiffeId))
        {
            return null;
        }

        var match = SpiffeIdPattern().Match(spiffeId);
        if (!match.Success)
        {
            return null;
        }

        var nodeIdString = match.Groups["nodeId"].Value;
        if (Guid.TryParse(nodeIdString, out var nodeId))
        {
            return nodeId;
        }

        return null;
    }

    private static bool HasClientAuthenticationEku(X509Certificate2 certificate)
    {
        var ekuExtension = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();

        if (ekuExtension is null)
        {
            return false;
        }

        return ekuExtension.EnhancedKeyUsages
            .OfType<Oid>()
            .Any(oid => oid.Value == ClientAuthenticationOid);
    }

    private static string CalculateSha256Thumbprint(X509Certificate2 certificate)
    {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(certificate.RawData);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
