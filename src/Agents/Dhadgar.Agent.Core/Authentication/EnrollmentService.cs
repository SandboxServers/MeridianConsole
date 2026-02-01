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
    public bool IsEnrolled => _options.NodeId.HasValue && _certificateStore.GetClientCertificate() != null;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(enrollmentToken);

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
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                // Truncate error content to avoid logging sensitive data
                var truncatedError = errorContent.Length > 200
                    ? errorContent[..200] + "..."
                    : errorContent;
                _logger.LogError("Enrollment failed with status {Status}: {Error}",
                    response.StatusCode, truncatedError);
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

            // Store the certificate
            var certBytes = Convert.FromBase64String(enrollmentResponse.Certificate);
            var cert = new X509Certificate2(certBytes);

            // Validate that the certificate's public key matches our locally generated keypair
            // This prevents the server from issuing a certificate for a different key
            using var certEcdsa = cert.GetECDsaPublicKey();
            if (certEcdsa is null)
            {
                cert.Dispose();
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.InvalidCertificate] Certificate does not contain an ECDSA public key");
            }

            var certPublicKeyBytes = certEcdsa.ExportSubjectPublicKeyInfo();
            var localPublicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
            if (!certPublicKeyBytes.AsSpan().SequenceEqual(localPublicKeyBytes))
            {
                cert.Dispose();
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.KeyMismatch] Certificate public key does not match locally generated keypair");
            }

            var privateKeyBytes = ecdsa.ExportECPrivateKey();

            await _certificateStore.StoreCertificateAsync(cert, privateKeyBytes, cancellationToken);

            // Store CA certificate if provided
            if (!string.IsNullOrEmpty(enrollmentResponse.CaCertificate))
            {
                var caBytes = Convert.FromBase64String(enrollmentResponse.CaCertificate);
                using var caCert = new X509Certificate2(caBytes);
                await _certificateStore.StoreCaCertificateAsync(caCert, cancellationToken);
            }

            _logger.LogInformation("Enrollment completed successfully. Node ID: {NodeId}",
                enrollmentResponse.NodeId);

            return Result<EnrollmentResult>.Success(new EnrollmentResult
            {
                NodeId = enrollmentResponse.NodeId,
                OrganizationId = enrollmentResponse.OrganizationId,
                CertificateExpiry = cert.NotAfter
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during enrollment");
            // Return sanitized error message without exception details
            return Result<EnrollmentResult>.Failure(
                "[Enrollment.NetworkError] Network error during enrollment");
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

        var currentCert = _certificateStore.GetClientCertificate();
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
            var response = await client.PostAsJsonAsync(
                $"api/v1/agents/{_options.NodeId}/certificates/renew",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                // Truncate error content to avoid logging sensitive data
                var truncatedError = errorContent.Length > 200
                    ? errorContent[..200] + "..."
                    : errorContent;
                _logger.LogError("Certificate renewal failed with status {Status}: {Error}",
                    response.StatusCode, truncatedError);
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

            // Store the new certificate
            var certBytes = Convert.FromBase64String(renewalResponse.Certificate);
            var newCert = new X509Certificate2(certBytes);

            // Validate that the certificate's public key matches our locally generated keypair
            // This prevents the server from issuing a certificate for a different key
            using var certEcdsa = newCert.GetECDsaPublicKey();
            if (certEcdsa is null)
            {
                newCert.Dispose();
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.InvalidCertificate] Certificate does not contain an ECDSA public key");
            }

            var certPublicKeyBytes = certEcdsa.ExportSubjectPublicKeyInfo();
            var localPublicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
            if (!certPublicKeyBytes.AsSpan().SequenceEqual(localPublicKeyBytes))
            {
                newCert.Dispose();
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.KeyMismatch] Certificate public key does not match locally generated keypair");
            }

            var privateKeyBytes = ecdsa.ExportECPrivateKey();

            await _certificateStore.StoreCertificateAsync(newCert, privateKeyBytes, cancellationToken);

            _logger.LogInformation("Certificate renewal completed. New expiry: {Expiry:O}",
                newCert.NotAfter);

            return Result<CertificateRenewalResult>.Success(new CertificateRenewalResult
            {
                NewExpiry = newCert.NotAfter,
                OldExpiry = currentCert.NotAfter
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during certificate renewal");
            // Return sanitized error message without exception details
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.NetworkError] Network error during renewal");
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
