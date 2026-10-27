using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services;

/// <summary>
/// Certificate Authority service for issuing and managing agent mTLS certificates.
/// Uses .NET built-in cryptography APIs (System.Security.Cryptography.X509Certificates).
/// </summary>
public sealed class CertificateAuthorityService : ICertificateAuthorityService, IDisposable
{
    private readonly ICaStorageProvider _storageProvider;
    private readonly NodesOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CertificateAuthorityService> _logger;
    // Singleton: never hold a NodesDbContext. Each lookup gets its own scope (P17f).
    private readonly IServiceScopeFactory _scopeFactory;

    // Cached CA certificate for performance
    private X509Certificate2? _caCertificate;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public CertificateAuthorityService(
        ICaStorageProvider storageProvider,
        IOptions<NodesOptions> options,
        TimeProvider timeProvider,
        ILogger<CertificateAuthorityService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _storageProvider = storageProvider;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct);
        try
        {
            if (_caCertificate is not null)
            {
                return; // Already initialized
            }

            var exists = await _storageProvider.ExistsAsync(ct);
            if (exists)
            {
                _logger.LogInformation("Loading existing CA certificate");
                _caCertificate = await _storageProvider.LoadAsync(ct);
                _logger.LogInformation(
                    "CA certificate loaded. Subject: {Subject}, NotAfter: {NotAfter}",
                    _caCertificate.Subject,
                    _caCertificate.NotAfter);
            }
            else
            {
                _logger.LogInformation("Creating new CA certificate");
                _caCertificate = CreateCaCertificate();
                await _storageProvider.StoreAsync(_caCertificate, ct);
                _logger.LogInformation(
                    "CA certificate created and stored. Subject: {Subject}, NotAfter: {NotAfter}",
                    _caCertificate.Subject,
                    _caCertificate.NotAfter);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<CertificateIssuanceResult> IssueCertificateAsync(
        Guid nodeId,
        CancellationToken ct = default)
    {
        X509Certificate2? certificate = null;
        RSA? privateKey = null;
        try
        {
            await EnsureInitializedAsync(ct);

            (certificate, privateKey) = CreateClientCertificate(nodeId);
            return CreateIssuanceResult(certificate, privateKey, nodeId);
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Cryptographic failure issuing certificate for node {NodeId}", nodeId);
            return CertificateIssuanceResult.Fail("certificate_generation_failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error issuing certificate for node {NodeId}", nodeId);
            return CertificateIssuanceResult.Fail("certificate_generation_failed");
        }
        finally
        {
            certificate?.Dispose();
            privateKey?.Dispose();
        }
    }

    public async Task<CertificateIssuanceResult> RenewCertificateAsync(
        Guid nodeId,
        string currentCertificateThumbprint,
        CancellationToken ct = default)
    {
        X509Certificate2? certificate = null;
        RSA? privateKey = null;
        try
        {
            await EnsureInitializedAsync(ct);

            // Validation of current certificate is done at the endpoint level
            // by checking the AgentCertificate table. Here we just issue a new cert.

            (certificate, privateKey) = CreateClientCertificate(nodeId);

            _logger.LogInformation(
                "Certificate renewed for node {NodeId}. Old thumbprint: {OldThumbprint}, New thumbprint: {NewThumbprint}",
                nodeId,
                currentCertificateThumbprint,
                certificate.Thumbprint);

            return CreateIssuanceResult(certificate, privateKey, nodeId);
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Cryptographic failure renewing certificate for node {NodeId}", nodeId);
            return CertificateIssuanceResult.Fail("certificate_renewal_failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error renewing certificate for node {NodeId}", nodeId);
            return CertificateIssuanceResult.Fail("certificate_renewal_failed");
        }
        finally
        {
            certificate?.Dispose();
            privateKey?.Dispose();
        }
    }

    public async Task<CertificateIssuanceResult> IssueCertificateForPublicKeyAsync(
        Guid nodeId,
        string subjectPublicKeyInfoBase64,
        CancellationToken ct = default)
    {
        PublicKey publicKey;
        X509KeyUsageFlags keyUsage;
        try
        {
            (publicKey, keyUsage) = ParseAgentPublicKey(subjectPublicKeyInfoBase64);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            _logger.LogWarning("Rejected agent public key for node {NodeId}: {Reason}", nodeId, ex.Message);
            return CertificateIssuanceResult.Fail("invalid_public_key");
        }

        try
        {
            await EnsureInitializedAsync(ct);

            if (_caCertificate is null)
            {
                throw new InvalidOperationException("CA certificate has not been initialized. Call InitializeAsync first.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var subject = new X500DistinguishedName($"CN={nodeId}, O={AgentCertificateProfile.Organization}");
            var request = new CertificateRequest(subject, publicKey, HashAlgorithmName.SHA256);
            AddClientCertificateExtensions(request, nodeId, keyUsage);

            var serialBytes = RandomNumberGenerator.GetBytes(16);
            serialBytes[0] &= 0x7F; // positive serial

            using var caPrivateKey = _caCertificate.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("CA certificate does not have an RSA private key");
            var signer = X509SignatureGenerator.CreateForRSA(caPrivateKey, RSASignaturePadding.Pkcs1);

            using var certificate = request.Create(
                _caCertificate.SubjectName,
                signer,
                now,
                now.AddDays(AgentCertificateProfile.ValidityDays),
                serialBytes);

            var thumbprint = CalculateSha256Thumbprint(certificate);
            var serialNumber = Convert.ToHexString(certificate.GetSerialNumber().Reverse().ToArray()).ToLowerInvariant();

            _logger.LogInformation(
                "Certificate issued for node {NodeId} (agent-held key). Thumbprint: {Thumbprint}, SerialNumber: {SerialNumber}, NotAfter: {NotAfter}",
                nodeId,
                thumbprint,
                serialNumber,
                certificate.NotAfter);

            return CertificateIssuanceResult.Ok(
                thumbprint,
                serialNumber,
                DateTime.SpecifyKind(certificate.NotBefore.ToUniversalTime(), DateTimeKind.Utc),
                DateTime.SpecifyKind(certificate.NotAfter.ToUniversalTime(), DateTimeKind.Utc),
                certificate.ExportCertificatePem());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to issue certificate for node {NodeId}", nodeId);
            return CertificateIssuanceResult.Fail("certificate_generation_failed");
        }
    }

    /// <summary>
    /// Parses and vets an agent-supplied SubjectPublicKeyInfo. Accepts ECDSA on P-256/P-384
    /// and RSA with at least 2048-bit modulus; everything else is rejected.
    /// </summary>
    private static (PublicKey Key, X509KeyUsageFlags KeyUsage) ParseAgentPublicKey(string subjectPublicKeyInfoBase64)
    {
        if (string.IsNullOrWhiteSpace(subjectPublicKeyInfoBase64))
        {
            throw new ArgumentException("Public key is empty.");
        }

        var spki = Convert.FromBase64String(subjectPublicKeyInfoBase64);
        var publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(spki, out var bytesRead);
        if (bytesRead != spki.Length)
        {
            throw new ArgumentException("Trailing data after SubjectPublicKeyInfo.");
        }

        using (var ecdsa = publicKey.GetECDsaPublicKey())
        {
            if (ecdsa is not null)
            {
                var curve = ecdsa.ExportParameters(false).Curve;
                var oid = curve.Oid?.Value;
                if (oid is not ("1.2.840.10045.3.1.7" or "1.3.132.0.34")) // P-256, P-384
                {
                    throw new ArgumentException("Unsupported ECDSA curve.");
                }

                // ECDSA keys sign; key encipherment is not valid for EC.
                return (publicKey, X509KeyUsageFlags.DigitalSignature);
            }
        }

        using (var rsa = publicKey.GetRSAPublicKey())
        {
            if (rsa is not null)
            {
                if (rsa.KeySize < 2048)
                {
                    throw new ArgumentException("RSA key too small.");
                }

                return (publicKey, X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment);
            }
        }

        throw new ArgumentException("Unsupported key algorithm.");
    }

    /// <summary>
    /// Standard extension set for agent client certificates (shared by all issuance paths).
    /// </summary>
    private void AddClientCertificateExtensions(CertificateRequest request, Guid nodeId, X509KeyUsageFlags keyUsage)
    {
        // Basic constraints - not a CA
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        // Key usage for client authentication
        request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, critical: true));

        // Extended key usage - client authentication
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.2")], // id-kp-clientAuth
                critical: true));

        // Subject Key Identifier
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Authority Key Identifier - link to CA
        request.CertificateExtensions.Add(
            CreateAuthorityKeyIdentifierExtension(_caCertificate!));

        // Subject Alternative Name with SPIFFE ID
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddUri(new Uri(AgentCertificateProfile.SpiffeId(nodeId)));
        request.CertificateExtensions.Add(sanBuilder.Build(critical: false));
    }

    public async Task<string> GetCaCertificatePemAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return await _storageProvider.GetCertificatePemAsync(ct);
    }

    public async Task<CertificateTrustStatus> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default)
    {
        try
        {
            await EnsureInitializedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Certificate validation unavailable: the CA certificate could not be loaded");
            return CertificateTrustStatus.Unavailable;
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPem(certificatePem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            _logger.LogWarning(ex, "Certificate validation failed: the certificate could not be parsed");
            return CertificateTrustStatus.Untrusted;
        }

        using (certificate)
        {
            // Chain first: no database round trip, so certificates from a foreign CA never reach the database.
            if (!IsIssuedByOurCa(certificate))
            {
                return CertificateTrustStatus.Untrusted;
            }

            var thumbprint = CalculateSha256Thumbprint(certificate);
            var (lookupSucceeded, agentCert) = await FindAgentCertificateAsync(thumbprint, ct);
            if (!lookupSucceeded)
            {
                return CertificateTrustStatus.Unavailable;
            }

            if (agentCert is not null && agentCert.IsRevoked)
            {
                _logger.LogWarning(
                    "Certificate validation failed: certificate {Thumbprint} is revoked (revoked at: {RevokedAt}, reason: {Reason})",
                    thumbprint,
                    agentCert.RevokedAt,
                    agentCert.RevocationReason);
                return CertificateTrustStatus.Untrusted;
            }

            return CertificateTrustStatus.Trusted;
        }
    }

    private bool IsIssuedByOurCa(X509Certificate2 certificate)
    {
        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            // The CA is supplied in ExtraStore, so no AIA or CDP download is ever needed. Block them so the
            // client-supplied certificate cannot make this process send outbound requests.
            chain.ChainPolicy.DisableCertificateDownloads = true;
            // Allow unknown CA (since our CA is self-signed and not in system trust store)
            // Ignore time validation (we handle expiration via database records)
            chain.ChainPolicy.VerificationFlags =
                X509VerificationFlags.AllowUnknownCertificateAuthority |
                X509VerificationFlags.IgnoreNotTimeValid;
            chain.ChainPolicy.ExtraStore.Add(_caCertificate!);

            var isValid = chain.Build(certificate);

            // A valid client certificate issued by our CA has 2 chain elements:
            // the client certificate (index 0) and our CA certificate (index 1).
            if (!isValid || chain.ChainElements.Count < 2)
            {
                return false;
            }

            var issuer = chain.ChainElements[^1].Certificate;
            return issuer.Thumbprint == _caCertificate!.Thumbprint;
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Certificate validation failed due to cryptographic error");
            return false;
        }
    }

    private async Task<(bool Succeeded, Data.Entities.AgentCertificate? Row)> FindAgentCertificateAsync(
        string thumbprint,
        CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Data.NodesDbContext>();
            var row = await dbContext.AgentCertificates
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Thumbprint == thumbprint, ct);
            return (true, row);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Infrastructure fault: report Unavailable (503), never Untrusted (401 invalid_issuer).
            _logger.LogError(ex, "Certificate revocation lookup failed for {Thumbprint}", thumbprint);
            return (false, null);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_caCertificate is null)
        {
            await InitializeAsync(ct);
        }

        if (_caCertificate is null)
        {
            throw new InvalidOperationException("CA certificate not initialized");
        }
    }

    private X509Certificate2 CreateCaCertificate()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Generate RSA key pair for CA
        using var rsa = RSA.Create(_options.CaKeySize);

        // Build the CA certificate request
        var subject = new X500DistinguishedName($"CN={AgentCertificateProfile.CaCommonName}, O={AgentCertificateProfile.Organization}");
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // CA certificate extensions
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: true,
                pathLengthConstraint: 0, // Can only sign end-entity certificates
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Self-sign the CA certificate
        var notBefore = now;
        var notAfter = now.AddYears(_options.CaValidityYears);

        using var caCert = request.CreateSelfSigned(notBefore, notAfter);

        // Export and reimport to ensure the certificate is fully usable
        var pfxBytes = caCert.Export(X509ContentType.Pfx);
        return new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.Exportable);
    }

    private (X509Certificate2 Certificate, RSA PrivateKey) CreateClientCertificate(Guid nodeId)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Generate RSA key pair for client
        var clientKey = RSA.Create(_options.ClientKeySize);

        try
        {
            // Build the client certificate request
            var subject = new X500DistinguishedName($"CN={nodeId}, O={AgentCertificateProfile.Organization}");
            var request = new CertificateRequest(subject, clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            AddClientCertificateExtensions(
                request,
                nodeId,
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment);

            // Generate serial number
            var serialBytes = RandomNumberGenerator.GetBytes(16);
            // Ensure the serial number is positive (MSB must be 0)
            serialBytes[0] &= 0x7F;

            // Sign with CA certificate - use fixed 90-day validity (security policy)
            var notBefore = now;
            var notAfter = now.AddDays(AgentCertificateProfile.ValidityDays);

            if (_caCertificate is null)
            {
                throw new InvalidOperationException("CA certificate has not been initialized. Call InitializeAsync first.");
            }

            using var caPrivateKey = _caCertificate.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("CA certificate does not have a private key");

            using var clientCert = request.Create(
                _caCertificate,
                notBefore,
                notAfter,
                serialBytes);

            // Combine the signed certificate with the client's private key
            var certWithKey = clientCert.CopyWithPrivateKey(clientKey);

            // Success - return both the certificate and the key (caller owns both)
            return (certWithKey, clientKey);
        }
        catch
        {
            // On any exception, dispose the client key to prevent resource leak
            clientKey.Dispose();
            throw;
        }
    }

    private CertificateIssuanceResult CreateIssuanceResult(
        X509Certificate2 certificate,
        RSA privateKey,
        Guid nodeId)
    {
        // Generate a secure password for the PKCS#12 bundle
        var pkcs12Password = GenerateSecurePassword();

        // Export certificate as PEM
        var certPem = certificate.ExportCertificatePem();

        // Export as PKCS#12 (includes both cert and private key)
        var pfxBytes = certificate.Export(X509ContentType.Pfx, pkcs12Password);
        var pkcs12Base64 = Convert.ToBase64String(pfxBytes);

        // Calculate SHA-256 thumbprint
        var thumbprint = CalculateSha256Thumbprint(certificate);

        // Get serial number as hex string
        var serialNumber = Convert.ToHexString(certificate.GetSerialNumber().Reverse().ToArray()).ToLowerInvariant();

        _logger.LogInformation(
            "Certificate issued for node {NodeId}. Thumbprint: {Thumbprint}, SerialNumber: {SerialNumber}, NotAfter: {NotAfter}",
            nodeId,
            thumbprint,
            serialNumber,
            certificate.NotAfter);

        return CertificateIssuanceResult.Ok(
            thumbprint,
            serialNumber,
            DateTime.SpecifyKind(certificate.NotBefore.ToUniversalTime(), DateTimeKind.Utc),
            DateTime.SpecifyKind(certificate.NotAfter.ToUniversalTime(), DateTimeKind.Utc),
            certPem,
            pkcs12Base64,
            pkcs12Password);
    }

    private static X509AuthorityKeyIdentifierExtension CreateAuthorityKeyIdentifierExtension(X509Certificate2 caCertificate)
    {
        // Get the Subject Key Identifier from the CA certificate
        var skiExtension = caCertificate.Extensions
            .OfType<X509SubjectKeyIdentifierExtension>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("CA certificate does not have a Subject Key Identifier");

        // Use built-in method to create AKI from SKI (.NET 7+)
        return X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(skiExtension);
    }

    private static string CalculateSha256Thumbprint(X509Certificate2 certificate)
    {
        var certBytes = certificate.RawData;
        var hashBytes = SHA256.HashData(certBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string GenerateSecurePassword()
    {
        // Generate a 32-byte random password and encode as base64
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Disposes the initialization semaphore and cached CA certificate.
    /// </summary>
    public void Dispose()
    {
        _initLock.Dispose();
        _caCertificate?.Dispose();
    }
}
