using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Shared.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Authentication;

/// <summary>
/// Handles initial agent enrollment with the control plane.
/// </summary>
public sealed class EnrollmentService : IEnrollmentService
{
    /// <summary>
    /// Maximum allowed size for base64-encoded certificate data.
    /// X.509 certificates are typically 1-2KB; 8KB encoded allows for generous headroom
    /// while preventing memory exhaustion from malicious payloads.
    /// </summary>
    private const int MaxCertificateBase64Length = 8192;

    private readonly ICertificateStore _certificateStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentOptions _options;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        ICertificateStore certificateStore,
        IHttpClientFactory httpClientFactory,
        IOptions<AgentOptions> options,
        ILogger<EnrollmentService> logger)
    {
        _certificateStore = certificateStore ?? throw new ArgumentNullException(nameof(certificateStore));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Check if the agent is enrolled.
    /// </summary>
    public bool IsEnrolled
    {
        get
        {
            if (!_options.NodeId.HasValue)
            {
                return false;
            }

            // SECURITY: Dispose certificate after checking to avoid handle leaks
            using var cert = _certificateStore.GetClientCertificate();
            return cert != null;
        }
    }

    /// <summary>
    /// Enroll the agent with the control plane using a one-time token.
    /// </summary>
    /// <param name="enrollmentToken">One-time enrollment token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enrollment result with assigned node ID.</returns>
    public async Task<Result<EnrollmentResult>> EnrollAsync(
        string enrollmentToken,
        CancellationToken cancellationToken = default)
    {
        // Validate input using Result pattern instead of throwing
        if (string.IsNullOrWhiteSpace(enrollmentToken))
        {
            return Result<EnrollmentResult>.Failure(
                "[Enrollment.InvalidToken] Enrollment token cannot be null or empty");
        }

        _logger.LogInformation("Starting enrollment with control plane");

        try
        {
            // Generate a key pair for the CSR
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();

            // Create enrollment request
            var request = new EnrollmentRequest
            {
                EnrollmentToken = enrollmentToken,
                NodeName = _options.NodeName,
                PublicKey = Convert.ToBase64String(publicKeyBytes),
                Platform = GetPlatformIdentifier(),
                AgentVersion = GetAgentVersion()
            };

            // Send enrollment request
            using var client = _httpClientFactory.CreateClient("ControlPlane");

            // Enforce HTTPS for enrollment - tokens should never traverse plain HTTP
            if (client.BaseAddress is null)
            {
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.ConfigError] Control plane base address not configured");
            }

            if (!string.Equals(client.BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Enrollment rejected: control plane must use HTTPS. Configured: {Scheme}",
                    client.BaseAddress.Scheme);
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.InsecureTransport] Enrollment requires HTTPS");
            }

            var response = await client.PostAsJsonAsync(
                "api/v1/agents/enroll",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // SECURITY: Do not log response body - may contain secrets or sensitive error details
                _logger.LogError("Enrollment failed with status {Status}", response.StatusCode);
                return Result<EnrollmentResult>.Failure(
                    $"[Enrollment.Failed] Enrollment failed: {response.StatusCode}");
            }

            var enrollmentResponse = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(
                cancellationToken: cancellationToken);

            if (enrollmentResponse is null)
            {
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.InvalidResponse] Invalid enrollment response from control plane");
            }

            // Validate required fields in enrollment response
            if (enrollmentResponse.NodeId == Guid.Empty)
            {
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.InvalidNodeId] Enrollment response contains empty NodeId");
            }

            if (enrollmentResponse.OrganizationId == Guid.Empty)
            {
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.InvalidOrganizationId] Enrollment response contains empty OrganizationId");
            }

            if (string.IsNullOrWhiteSpace(enrollmentResponse.Certificate))
            {
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.InvalidCertificate] Enrollment response contains empty certificate");
            }

            // Validate certificate size to prevent memory exhaustion
            if (enrollmentResponse.Certificate.Length > MaxCertificateBase64Length)
            {
                _logger.LogWarning("Certificate data exceeds maximum size ({Length} > {Max})",
                    enrollmentResponse.Certificate.Length, MaxCertificateBase64Length);
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.CertificateTooLarge] Certificate data exceeds maximum allowed size");
            }

            // Store the certificate (use X509CertificateLoader for security)
            var certBytes = Convert.FromBase64String(enrollmentResponse.Certificate);
            DateTime certExpiry;

            // Use using to guarantee certificate disposal on all paths
            using (var cert = X509CertificateLoader.LoadCertificate(certBytes))
            {
                // Validate that the certificate's public key matches our locally generated keypair
                // This prevents the server from issuing a certificate for a different key
                using var certEcdsa = cert.GetECDsaPublicKey();
                if (certEcdsa is null)
                {
                    return Result<EnrollmentResult>.Failure(
                        "[Enrollment.InvalidCertificate] Certificate does not contain an ECDSA public key");
                }

                var certPublicKeyBytes = certEcdsa.ExportSubjectPublicKeyInfo();
                var localPublicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
                if (!certPublicKeyBytes.AsSpan().SequenceEqual(localPublicKeyBytes))
                {
                    return Result<EnrollmentResult>.Failure(
                        "[Enrollment.KeyMismatch] Certificate public key does not match locally generated keypair");
                }

                // Capture expiry before cert is disposed
                certExpiry = cert.NotAfter;

                var privateKeyBytes = ecdsa.ExportECPrivateKey();

                try
                {
                    await _certificateStore.StoreCertificateAsync(cert, privateKeyBytes, cancellationToken);
                }
                finally
                {
                    // SECURITY: Zero sensitive key material immediately after use
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(privateKeyBytes);
                }
            }

            // Store CA certificate if provided
            if (!string.IsNullOrEmpty(enrollmentResponse.CaCertificate))
            {
                // Validate CA certificate size
                if (enrollmentResponse.CaCertificate.Length > MaxCertificateBase64Length)
                {
                    _logger.LogWarning("CA certificate data exceeds maximum size ({Length} > {Max})",
                        enrollmentResponse.CaCertificate.Length, MaxCertificateBase64Length);
                    return Result<EnrollmentResult>.Failure(
                        "[Enrollment.CaCertificateTooLarge] CA certificate data exceeds maximum allowed size");
                }

                var caBytes = Convert.FromBase64String(enrollmentResponse.CaCertificate);
                using var caCert = X509CertificateLoader.LoadCertificate(caBytes);
                await _certificateStore.StoreCaCertificateAsync(caCert, cancellationToken);
            }

            _logger.LogInformation("Enrollment completed successfully. Node ID: {NodeId}",
                enrollmentResponse.NodeId);

            return Result<EnrollmentResult>.Success(new EnrollmentResult
            {
                NodeId = enrollmentResponse.NodeId,
                OrganizationId = enrollmentResponse.OrganizationId,
                CertificateExpiry = certExpiry
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during enrollment");
            return Result<EnrollmentResult>.Failure(
                "[Enrollment.NetworkError] Network error during enrollment");
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Invalid base64 data in enrollment response");
            return Result<EnrollmentResult>.Failure(
                "[Enrollment.InvalidFormat] Invalid certificate data format");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogError(ex, "Cryptographic error during enrollment");
            return Result<EnrollmentResult>.Failure(
                "[Enrollment.CryptoError] Certificate processing failed");
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in enrollment response");
            return Result<EnrollmentResult>.Failure(
                "[Enrollment.InvalidResponse] Invalid enrollment response format");
        }
    }

    /// <summary>
    /// Renew the agent certificate.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Renewal result.</returns>
    public async Task<Result<CertificateRenewalResult>> RenewCertificateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.NodeId.HasValue)
        {
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.NotEnrolled] Agent is not enrolled");
        }

        // SECURITY: Use 'using' to ensure certificate is disposed after use
        using var currentCert = _certificateStore.GetClientCertificate();
        if (currentCert is null)
        {
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.NoCertificate] No current certificate found");
        }

        _logger.LogInformation("Starting certificate renewal for node {NodeId}", _options.NodeId);

        try
        {
            // Generate new key pair
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();

            var request = new CertificateRenewalRequest
            {
                NodeId = _options.NodeId.Value,
                PublicKey = Convert.ToBase64String(publicKeyBytes)
            };

            // Use mTLS client for renewal (includes current certificate)
            using var client = _httpClientFactory.CreateClient("ControlPlaneMtls");

            // Enforce HTTPS for renewal - certificate material should never traverse plain HTTP
            if (client.BaseAddress is null)
            {
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.ConfigError] Control plane base address not configured");
            }

            if (!string.Equals(client.BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Certificate renewal rejected: control plane must use HTTPS. Configured: {Scheme}",
                    client.BaseAddress.Scheme);
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.InsecureTransport] Certificate renewal requires HTTPS");
            }

            var response = await client.PostAsJsonAsync(
                $"api/v1/agents/{_options.NodeId}/certificates/renew",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // SECURITY: Do not log response body - may contain secrets or sensitive error details
                _logger.LogError("Certificate renewal failed with status {Status}", response.StatusCode);
                return Result<CertificateRenewalResult>.Failure(
                    $"[Renewal.Failed] Certificate renewal failed: {response.StatusCode}");
            }

            var renewalResponse = await response.Content.ReadFromJsonAsync<CertificateRenewalResponse>(
                cancellationToken: cancellationToken);

            if (renewalResponse is null)
            {
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.InvalidResponse] Invalid renewal response from control plane");
            }

            // Validate certificate field is not empty
            if (string.IsNullOrWhiteSpace(renewalResponse.Certificate))
            {
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.InvalidCertificate] Renewal response contains empty certificate");
            }

            // Validate certificate size to prevent memory exhaustion
            if (renewalResponse.Certificate.Length > MaxCertificateBase64Length)
            {
                _logger.LogWarning("Renewal certificate data exceeds maximum size ({Length} > {Max})",
                    renewalResponse.Certificate.Length, MaxCertificateBase64Length);
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.CertificateTooLarge] Certificate data exceeds maximum allowed size");
            }

            // Store the new certificate (use X509CertificateLoader for security)
            var certBytes = Convert.FromBase64String(renewalResponse.Certificate);
            DateTime newExpiry;

            // Use using to guarantee certificate disposal on all paths
            using (var newCert = X509CertificateLoader.LoadCertificate(certBytes))
            {
                // Validate that the certificate's public key matches our locally generated keypair
                // This prevents the server from issuing a certificate for a different key
                using var certEcdsa = newCert.GetECDsaPublicKey();
                if (certEcdsa is null)
                {
                    return Result<CertificateRenewalResult>.Failure(
                        "[Renewal.InvalidCertificate] Certificate does not contain an ECDSA public key");
                }

                var certPublicKeyBytes = certEcdsa.ExportSubjectPublicKeyInfo();
                var localPublicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
                if (!certPublicKeyBytes.AsSpan().SequenceEqual(localPublicKeyBytes))
                {
                    return Result<CertificateRenewalResult>.Failure(
                        "[Renewal.KeyMismatch] Certificate public key does not match locally generated keypair");
                }

                // Capture expiry before cert is disposed
                newExpiry = newCert.NotAfter;

                var privateKeyBytes = ecdsa.ExportECPrivateKey();

                try
                {
                    await _certificateStore.StoreCertificateAsync(newCert, privateKeyBytes, cancellationToken);
                }
                finally
                {
                    // SECURITY: Zero sensitive key material immediately after use
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(privateKeyBytes);
                }
            }

            _logger.LogInformation("Certificate renewal completed. New expiry: {Expiry:O}", newExpiry);

            return Result<CertificateRenewalResult>.Success(new CertificateRenewalResult
            {
                NewExpiry = newExpiry,
                OldExpiry = currentCert.NotAfter
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during certificate renewal");
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.NetworkError] Network error during renewal");
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Invalid base64 data in renewal response");
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.InvalidFormat] Invalid certificate data format");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogError(ex, "Cryptographic error during certificate renewal");
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.CryptoError] Certificate processing failed");
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in renewal response");
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.InvalidResponse] Invalid renewal response format");
        }
    }

    private static string GetPlatformIdentifier()
    {
        return OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "macos"
            : "unknown";
    }

    private static string GetAgentVersion()
    {
        var assembly = typeof(EnrollmentService).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }
}

/// <summary>
/// Enrollment request sent to the control plane.
/// </summary>
public sealed class EnrollmentRequest
{
    public required string EnrollmentToken { get; init; }
    public string? NodeName { get; init; }
    public required string PublicKey { get; init; }
    public required string Platform { get; init; }
    public required string AgentVersion { get; init; }
}

/// <summary>
/// Enrollment response from the control plane.
/// </summary>
public sealed class EnrollmentResponse
{
    public required Guid NodeId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string Certificate { get; init; }
    public string? CaCertificate { get; init; }
}

/// <summary>
/// Successful enrollment result.
/// </summary>
public sealed class EnrollmentResult
{
    public required Guid NodeId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required DateTime CertificateExpiry { get; init; }
}

/// <summary>
/// Certificate renewal request.
/// </summary>
public sealed class CertificateRenewalRequest
{
    public required Guid NodeId { get; init; }
    public required string PublicKey { get; init; }
}

/// <summary>
/// Certificate renewal response.
/// </summary>
public sealed class CertificateRenewalResponse
{
    public required string Certificate { get; init; }
}

/// <summary>
/// Certificate renewal result.
/// </summary>
public sealed class CertificateRenewalResult
{
    public required DateTime NewExpiry { get; init; }
    public required DateTime OldExpiry { get; init; }
}
