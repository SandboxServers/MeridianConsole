using System.Formats.Asn1;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Contracts.Nodes;
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

    /// <summary>
    /// The control plane serializes with ASP.NET Core web defaults (camelCase).
    /// </summary>
    private static readonly JsonSerializerOptions WireJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Maximum allowed response body size (1 MB).
    /// Prevents memory exhaustion from malicious control plane responses.
    /// </summary>
    private const int MaxResponseBodySize = 1024 * 1024;

    /// <summary>
    /// Maximum size read from a non-success body when looking for its errorCode (4 KB).
    /// </summary>
    private const int MaxErrorBodySize = 4 * 1024;

    private readonly ICertificateStore _certificateStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentOptions _options;
    private readonly ILogger<EnrollmentService> _logger;
    private readonly IEnrollmentTokenCleanup? _enrollmentTokenCleanup;
    private readonly IHardwareInventoryCollector _hardwareInventory;
    private readonly IClientCertificateProvider _clientCertificateProvider;

    /// <param name="clientCertificateProvider">
    /// Invalidated after enrollment and renewal store new key material. When omitted, a provider
    /// over <paramref name="certificateStore"/> is created, which is only correct when no other
    /// handshake consumer shares the store.
    /// </param>
    public EnrollmentService(
        ICertificateStore certificateStore,
        IHttpClientFactory httpClientFactory,
        IOptions<AgentOptions> options,
        ILogger<EnrollmentService> logger,
        IHardwareInventoryCollector hardwareInventory,
        IEnrollmentTokenCleanup? enrollmentTokenCleanup = null,
        IClientCertificateProvider? clientCertificateProvider = null)
    {
        _hardwareInventory = hardwareInventory ?? throw new ArgumentNullException(nameof(hardwareInventory));
        _certificateStore = certificateStore ?? throw new ArgumentNullException(nameof(certificateStore));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enrollmentTokenCleanup = enrollmentTokenCleanup;
        _clientCertificateProvider = clientCertificateProvider ?? new ClientCertificateProvider(certificateStore);
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

            // Create enrollment request (shared contract with Dhadgar.Nodes)
            var request = new EnrollNodeRequest(
                Token: enrollmentToken,
                Platform: GetPlatformIdentifier(),
                Hardware: _hardwareInventory.Collect(),
                PublicKey: Convert.ToBase64String(publicKeyBytes),
                AgentVersion: GetAgentVersion());

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

            // SECURITY: Use ResponseHeadersRead to check Content-Length before buffering entire body
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/v1/agents/enroll")
            {
                Content = JsonContent.Create(request, options: WireJsonOptions)
            };

            using var response = await client.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // SECURITY: Do not log response body - may contain secrets or sensitive error details
                _logger.LogError("Enrollment failed with status {Status}", response.StatusCode);
                return Result<EnrollmentResult>.Failure(
                    $"[Enrollment.Failed] Enrollment failed: {response.StatusCode}");
            }

            // SECURITY: Validate Content-Length before reading body to prevent memory exhaustion
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxResponseBodySize)
            {
                _logger.LogWarning("Enrollment response too large: {Length} bytes (max: {Max})",
                    contentLength.Value, MaxResponseBodySize);
                return Result<EnrollmentResult>.Failure(
                    "[Enrollment.ResponseTooLarge] Response exceeds maximum allowed size");
            }

            // Read with size-limited stream if Content-Length is missing or needs verification
            EnrollNodeResponse? enrollmentResponse;
            await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var limitedStream = new SizeLimitedStream(responseStream, MaxResponseBodySize))
            {
                enrollmentResponse = await JsonSerializer.DeserializeAsync<EnrollNodeResponse>(
                    limitedStream,
                    WireJsonOptions,
                    cancellationToken);
            }

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

            // Store the certificate. Nodes returns PEM; DecodeCertificate only accepts a single
            // CERTIFICATE block and loads it via X509CertificateLoader (no PKCS#12/key material).
            DateTime certExpiry;

            // Use using to guarantee certificate disposal on all paths
            using (var cert = DecodeCertificate(enrollmentResponse.Certificate))
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

                // PKCS#8 on every platform; the store and the mTLS provider both expect this format
                var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();

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

            _clientCertificateProvider.Invalidate();

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

                using var caCert = DecodeCertificate(enrollmentResponse.CaCertificate);
                await _certificateStore.StoreCaCertificateAsync(caCert, cancellationToken);
            }

            _logger.LogInformation("Enrollment completed successfully. Node ID: {NodeId}",
                enrollmentResponse.NodeId);

            // SECURITY: Clean up the enrollment token from platform-specific storage
            // This prevents token reuse attacks and exposure via registry/file inspection
            // Best-effort: cleanup failure should not turn successful enrollment into failure
            try
            {
                _enrollmentTokenCleanup?.CleanupEnrollmentToken();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up enrollment token after successful enrollment");
            }

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
        catch (InvalidOperationException ex) when (ex.Message.Contains("exceeded maximum size", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "Enrollment response exceeded maximum allowed size");
            return Result<EnrollmentResult>.Failure(
                "[Enrollment.ResponseTooLarge] Response body exceeded maximum allowed size");
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
        // NodeId comes from options; P11 replaces this with the identity provider
        if (!_options.NodeId.HasValue)
        {
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.NotEnrolled] Agent is not enrolled");
        }

        var nodeId = _options.NodeId.Value;

        // SECURITY: Use 'using' to ensure certificate is disposed after use
        using var currentCert = _certificateStore.GetClientCertificate();
        if (currentCert is null)
        {
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.NoCertificate] No current certificate found");
        }

        _logger.LogInformation("Starting certificate renewal for node {NodeId}", nodeId);

        try
        {
            // The new key pair is generated here and never leaves the agent; only the public key is sent
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var request = new RenewCertificateRequest(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));

            // Use mTLS client for renewal (the handshake presents the current certificate)
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

            // SECURITY: Use ResponseHeadersRead to check Content-Length before buffering entire body
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"api/v1/agents/{nodeId:D}/certificates/renew")
            {
                Content = JsonContent.Create(request, options: WireJsonOptions)
            };

            using var response = await client.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // SECURITY: Do not log the body; only the bounded, plain-token errorCode is surfaced
                var errorCode = await ReadProblemErrorCodeAsync(response, cancellationToken);
                _logger.LogError("Certificate renewal failed with status {Status} ({ErrorCode})",
                    response.StatusCode, errorCode ?? "none");
                return Result<CertificateRenewalResult>.Failure(
                    $"[Renewal.Rejected] {(int)response.StatusCode} {errorCode ?? "unknown"}");
            }

            // SECURITY: Validate Content-Length before reading body to prevent memory exhaustion
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxResponseBodySize)
            {
                _logger.LogWarning("Renewal response too large: {Length} bytes (max: {Max})",
                    contentLength.Value, MaxResponseBodySize);
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.ResponseTooLarge] Response exceeds maximum allowed size");
            }

            // Read with size-limited stream if Content-Length is missing or needs verification
            RenewCertificateResponse? renewalResponse;
            await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var limitedStream = new SizeLimitedStream(responseStream, MaxResponseBodySize))
            {
                renewalResponse = await JsonSerializer.DeserializeAsync<RenewCertificateResponse>(
                    limitedStream,
                    WireJsonOptions,
                    cancellationToken);
            }

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

            // Validate certificate size to prevent memory exhaustion (PEM fits within the limit)
            if (renewalResponse.Certificate.Length > MaxCertificateBase64Length)
            {
                _logger.LogWarning("Renewal certificate data exceeds maximum size ({Length} > {Max})",
                    renewalResponse.Certificate.Length, MaxCertificateBase64Length);
                return Result<CertificateRenewalResult>.Failure(
                    "[Renewal.CertificateTooLarge] Certificate data exceeds maximum allowed size");
            }

            DateTime newExpiry;
            var localPublicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();

            // Use using to guarantee certificate disposal on all paths
            using (var newCert = DecodeCertificate(renewalResponse.Certificate))
            {
                // The certificate must bind exactly the key this agent generated for this renewal
                using (var certEcdsa = newCert.GetECDsaPublicKey())
                {
                    if (certEcdsa is null)
                    {
                        return Result<CertificateRenewalResult>.Failure(
                            "[Renewal.InvalidCertificate] Certificate does not contain an ECDSA public key");
                    }

                    if (!certEcdsa.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(localPublicKeyBytes))
                    {
                        return Result<CertificateRenewalResult>.Failure(
                            "[Renewal.KeyMismatch] Certificate public key does not match locally generated keypair");
                    }
                }

                // The certificate must name this node and no other identity
                if (!HasOnlySpiffeUri(newCert, AgentCertificateProfile.SpiffeId(nodeId)))
                {
                    _logger.LogError("Renewed certificate does not carry the expected SPIFFE identity for node {NodeId}",
                        nodeId);
                    return Result<CertificateRenewalResult>.Failure(
                        "[Renewal.WrongIdentity] Certificate SAN does not match this node");
                }

                // Capture expiry before cert is disposed
                newExpiry = newCert.NotAfter;

                // PKCS#8 on every platform, matching the enrollment handoff
                var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();

                try
                {
                    await _certificateStore.StoreCertificateAsync(newCert, privateKeyBytes, cancellationToken);
                }
                finally
                {
                    // SECURITY: Zero sensitive key material immediately after use
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(privateKeyBytes);
                }

                // Handshakes must present the new certificate from now on
                _clientCertificateProvider.Invalidate();
            }

            // Store CA certificate if provided
            if (!string.IsNullOrEmpty(renewalResponse.CaCertificate))
            {
                if (renewalResponse.CaCertificate.Length > MaxCertificateBase64Length)
                {
                    _logger.LogWarning("CA certificate data exceeds maximum size ({Length} > {Max})",
                        renewalResponse.CaCertificate.Length, MaxCertificateBase64Length);
                    return Result<CertificateRenewalResult>.Failure(
                        "[Renewal.CaCertificateTooLarge] CA certificate data exceeds maximum allowed size");
                }

                using var caCert = DecodeCertificate(renewalResponse.CaCertificate);
                await _certificateStore.StoreCaCertificateAsync(caCert, cancellationToken);
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
        catch (InvalidOperationException ex) when (ex.Message.Contains("exceeded maximum size", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "Renewal response exceeded maximum allowed size");
            return Result<CertificateRenewalResult>.Failure(
                "[Renewal.ResponseTooLarge] Response body exceeded maximum allowed size");
        }
    }

    /// <summary>
    /// Reads the <c>errorCode</c> extension of a problem+json error body. Returns null when the body
    /// is absent, malformed, oversized, or the value is not a short plain token.
    /// </summary>
    private static async Task<string?> ReadProblemErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var limitedStream = new SizeLimitedStream(responseStream, MaxErrorBodySize);
            using var document = await JsonDocument.ParseAsync(limitedStream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errorCode", out var element)
                || element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = element.GetString();
            if (value is null || value.Length is 0 or > 64)
            {
                return null;
            }

            // Only [a-z0-9_] are kept so a hostile body cannot inject text into the error or the log
            return value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? value : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when the certificate's only URI SAN is <paramref name="expectedUri"/>.
    /// </summary>
    private static bool HasOnlySpiffeUri(X509Certificate2 certificate, string expectedUri)
    {
        var uriCount = 0;
        var matches = false;

        foreach (var extension in certificate.Extensions)
        {
            // subjectAltName
            if (extension.Oid?.Value != "2.5.29.17")
            {
                continue;
            }

            var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
            var names = reader.ReadSequence();
            while (names.HasData)
            {
                // GeneralName uniformResourceIdentifier is [6] IA5String
                if (names.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 6)))
                {
                    var uri = names.ReadCharacterString(
                        UniversalTagNumber.IA5String,
                        new Asn1Tag(TagClass.ContextSpecific, 6));
                    uriCount++;
                    matches = string.Equals(uri, expectedUri, StringComparison.Ordinal);
                }
                else
                {
                    names.ReadEncodedValue();
                }
            }
        }

        return uriCount == 1 && matches;
    }

    /// <summary>
    /// Decodes a certificate sent by the control plane. Accepts a single PEM
    /// <c>CERTIFICATE</c> block (what Nodes returns) or bare base64 DER.
    /// </summary>
    /// <exception cref="FormatException">Malformed PEM/base64.</exception>
    /// <exception cref="CryptographicException">Not a valid X.509 certificate.</exception>
    private static X509Certificate2 DecodeCertificate(string encoded)
    {
        byte[] der;
        if (encoded.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            if (!PemEncoding.TryFind(encoded, out var fields)
                || !encoded.AsSpan()[fields.Label].SequenceEqual("CERTIFICATE"))
            {
                throw new FormatException("Expected a PEM CERTIFICATE block");
            }

            der = Convert.FromBase64String(encoded[fields.Base64Data]);
        }
        else
        {
            der = Convert.FromBase64String(encoded);
        }

        return X509CertificateLoader.LoadCertificate(der);
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
/// Successful enrollment result.
/// </summary>
public sealed class EnrollmentResult
{
    public required Guid NodeId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required DateTime CertificateExpiry { get; init; }
}

/// <summary>
/// Certificate renewal result.
/// </summary>
public sealed class CertificateRenewalResult
{
    public required DateTime NewExpiry { get; init; }
    public required DateTime OldExpiry { get; init; }
}

/// <summary>
/// A stream wrapper that enforces a maximum read limit.
/// Throws <see cref="InvalidOperationException"/> if the underlying stream exceeds the limit.
/// </summary>
internal sealed class SizeLimitedStream : Stream
{
    private readonly Stream _innerStream;
    private readonly long _maxSize;
    private long _totalBytesRead;

    public SizeLimitedStream(Stream innerStream, long maxSize)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _maxSize = maxSize;
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _totalBytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _innerStream.Read(buffer, offset, count);
        _totalBytesRead += bytesRead;

        if (_totalBytesRead > _maxSize)
        {
            throw new InvalidOperationException(
                $"Stream exceeded maximum size of {_maxSize} bytes");
        }

        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var bytesRead = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        _totalBytesRead += bytesRead;

        if (_totalBytesRead > _maxSize)
        {
            throw new InvalidOperationException(
                $"Stream exceeded maximum size of {_maxSize} bytes");
        }

        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken);
        _totalBytesRead += bytesRead;

        if (_totalBytesRead > _maxSize)
        {
            throw new InvalidOperationException(
                $"Stream exceeded maximum size of {_maxSize} bytes");
        }

        return bytesRead;
    }

    public override void Flush() => _innerStream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _innerStream.DisposeAsync();
        await base.DisposeAsync();
    }
}
