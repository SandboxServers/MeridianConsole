using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.Services;

/// <summary>
/// Certificate Authority service for issuing and managing agent mTLS certificates.
/// Uses .NET built-in cryptography APIs (System.Security.Cryptography.X509Certificates).
/// </summary>
public sealed class CertificateAuthorityService : ICertificateAuthorityService
{
    private readonly ICaStorageProvider _storageProvider;
    private readonly NodesOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CertificateAuthorityService> _logger;

    // SPIFFE ID format for agent certificates
    private const string SpiffeIdFormat = "spiffe://meridianconsole.com/nodes/{0}";
    private const string OrganizationName = "MeridianConsole";
    private const string CaCommonName = "Meridian Console Agent CA";

    // Cached CA certificate for performance
    private X509Certificate2? _caCertificate;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public CertificateAuthorityService(
        ICaStorageProvider storageProvider,
        IOptions<NodesOptions> options,
        TimeProvider timeProvider,
        ILogger<CertificateAuthorityService> logger)
    {
        _storageProvider = storageProvider;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
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
        try
        {
            await EnsureInitializedAsync(ct);

            var (certificate, privateKey) = CreateClientCertificate(nodeId);
            return CreateIssuanceResult(certificate, privateKey, nodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to issue certificate for node {NodeId}", nodeId);
            return CertificateIssuanceResult.Fail($"certificate_generation_failed: {ex.Message}");
        }
    }

    public async Task<CertificateIssuanceResult> RenewCertificateAsync(
        Guid nodeId,
        string currentCertificateThumbprint,
        CancellationToken ct = default)
    {
        try
        {
            await EnsureInitializedAsync(ct);

            // Validation of current certificate is done at the endpoint level
            // by checking the AgentCertificate table. Here we just issue a new cert.

            var (certificate, privateKey) = CreateClientCertificate(nodeId);

            _logger.LogInformation(
                "Certificate renewed for node {NodeId}. Old thumbprint: {OldThumbprint}, New thumbprint: {NewThumbprint}",
                nodeId,
                currentCertificateThumbprint,
                certificate.Thumbprint);

            return CreateIssuanceResult(certificate, privateKey, nodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew certificate for node {NodeId}", nodeId);
            return CertificateIssuanceResult.Fail($"certificate_renewal_failed: {ex.Message}");
        }
    }

    public async Task<string> GetCaCertificatePemAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return await _storageProvider.GetCertificatePemAsync(ct);
    }

    public async Task<bool> ValidateCertificateAsync(string certificatePem, CancellationToken ct = default)
    {
        try
        {
            await EnsureInitializedAsync(ct);

            var certificate = X509Certificate2.CreateFromPem(certificatePem);

            // Build certificate chain and validate against CA
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(_caCertificate!);

            var isValid = chain.Build(certificate);

            // Additional check: verify the certificate was actually signed by our CA
            if (isValid && chain.ChainElements.Count >= 2)
            {
                var issuer = chain.ChainElements[^1].Certificate;
                isValid = issuer.Thumbprint == _caCertificate!.Thumbprint;
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Certificate validation failed");
            return false;
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
        var subject = new X500DistinguishedName($"CN={CaCommonName}, O={OrganizationName}");
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

        var caCert = request.CreateSelfSigned(notBefore, notAfter);

        // Export and reimport to ensure the certificate is fully usable
        var pfxBytes = caCert.Export(X509ContentType.Pfx);
        return new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.Exportable);
    }

    private (X509Certificate2 Certificate, RSA PrivateKey) CreateClientCertificate(Guid nodeId)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Generate RSA key pair for client
        var clientKey = RSA.Create(_options.ClientKeySize);

        // Build the client certificate request
        var subject = new X500DistinguishedName($"CN={nodeId}, O={OrganizationName}");
        var request = new CertificateRequest(subject, clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Client certificate extensions

        // Basic constraints - not a CA
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        // Key usage for client authentication
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

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
        var spiffeId = string.Format(SpiffeIdFormat, nodeId);
        sanBuilder.AddUri(new Uri(spiffeId));
        request.CertificateExtensions.Add(sanBuilder.Build(critical: false));

        // Generate serial number
        var serialBytes = RandomNumberGenerator.GetBytes(16);
        // Ensure the serial number is positive (MSB must be 0)
        serialBytes[0] &= 0x7F;

        // Sign with CA certificate
        var notBefore = now;
        var notAfter = now.AddDays(_options.CertificateValidityDays);

        using var caPrivateKey = _caCertificate!.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("CA certificate does not have a private key");

        var clientCert = request.Create(
            _caCertificate,
            notBefore,
            notAfter,
            serialBytes);

        // Combine the signed certificate with the client's private key
        var certWithKey = clientCert.CopyWithPrivateKey(clientKey);

        return (certWithKey, clientKey);
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
            certificate.NotBefore,
            certificate.NotAfter,
            certPem,
            pkcs12Base64,
            pkcs12Password);
    }

    private static X509Extension CreateAuthorityKeyIdentifierExtension(X509Certificate2 caCertificate)
    {
        // Get the Subject Key Identifier from the CA certificate
        var skiExtension = caCertificate.Extensions
            .OfType<X509SubjectKeyIdentifierExtension>()
            .FirstOrDefault();

        if (skiExtension is null)
        {
            throw new InvalidOperationException("CA certificate does not have a Subject Key Identifier");
        }

        // Build the Authority Key Identifier
        // OID: 2.5.29.35
        // Format: SEQUENCE { [0] OCTET STRING (key identifier) }
        var keyId = HexStringToBytes(skiExtension.SubjectKeyIdentifier!);

        var builder = new AsnWriter(AsnEncodingRules.DER);
        using (builder.PushSequence())
        {
            builder.WriteOctetString(keyId, new Asn1Tag(TagClass.ContextSpecific, 0));
        }

        return new X509Extension(
            new Oid("2.5.29.35", "Authority Key Identifier"),
            builder.Encode(),
            critical: false);
    }

    private static byte[] HexStringToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
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
}
