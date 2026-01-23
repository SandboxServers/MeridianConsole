using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Nodes.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Dhadgar.Nodes.Tests;

public sealed class CertificateAuthorityServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly FakeTimeProvider _timeProvider;
    private readonly NodesOptions _options;
    private readonly IOptions<NodesOptions> _optionsWrapper;

    public CertificateAuthorityServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ca-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        _options = new NodesOptions
        {
            CaStoragePath = _tempDirectory,
            CaKeySize = 2048, // Smaller for faster tests
            ClientKeySize = 2048,
            CaValidityYears = 10,
            CertificateValidityDays = 90
        };
        _optionsWrapper = Options.Create(_options);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    private CertificateAuthorityService CreateService(ICaStorageProvider? storageProvider = null)
    {
        storageProvider ??= new LocalFileCaStorageProvider(
            _optionsWrapper,
            NullLogger<LocalFileCaStorageProvider>.Instance);

        return new CertificateAuthorityService(
            storageProvider,
            _optionsWrapper,
            _timeProvider,
            NullLogger<CertificateAuthorityService>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_CreatesNewCa_WhenNoExistingCa()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.InitializeAsync();

        // Assert - CA certificate should be created
        var caPem = await service.GetCaCertificatePemAsync();
        Assert.NotNull(caPem);
        Assert.Contains("BEGIN CERTIFICATE", caPem);
        Assert.Contains("END CERTIFICATE", caPem);
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingCa_WhenCaExists()
    {
        // Arrange - Initialize once to create CA
        var service1 = CreateService();
        await service1.InitializeAsync();
        var caPem1 = await service1.GetCaCertificatePemAsync();

        // Act - Create new service instance and initialize
        var service2 = CreateService();
        await service2.InitializeAsync();
        var caPem2 = await service2.GetCaCertificatePemAsync();

        // Assert - Should load same CA
        Assert.Equal(caPem1, caPem2);
    }

    [Fact]
    public async Task IssueCertificateAsync_GeneratesValidCertificate()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Thumbprint);
        Assert.NotNull(result.SerialNumber);
        Assert.NotNull(result.CertificatePem);
        Assert.NotNull(result.Pkcs12Base64);
        Assert.NotNull(result.Pkcs12Password);
        Assert.Equal(_timeProvider.GetUtcNow().UtcDateTime, result.NotBefore);
        Assert.Equal(_timeProvider.GetUtcNow().UtcDateTime.AddDays(90), result.NotAfter);
    }

    [Fact]
    public async Task IssueCertificateAsync_CertificateHasCorrectSubject()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        var certPem = result.CertificatePem!;
        var cert = X509Certificate2.CreateFromPem(certPem);

        Assert.Contains($"CN={nodeId}", cert.Subject);
        Assert.Contains("O=MeridianConsole", cert.Subject);
    }

    [Fact]
    public async Task IssueCertificateAsync_CertificateHasSpiffeId()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        var certPem = result.CertificatePem!;
        var cert = X509Certificate2.CreateFromPem(certPem);

        // Check Subject Alternative Name extension contains SPIFFE ID
        var sanExtension = cert.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .FirstOrDefault();

        Assert.NotNull(sanExtension);

        // The SAN should contain the SPIFFE URI
        var sanString = sanExtension.Format(multiLine: true);
        Assert.Contains($"spiffe://meridianconsole.com/nodes/{nodeId}", sanString);
    }

    [Fact]
    public async Task IssueCertificateAsync_CertificateHasClientAuthEku()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        var certPem = result.CertificatePem!;
        var cert = X509Certificate2.CreateFromPem(certPem);

        var ekuExtension = cert.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();

        Assert.NotNull(ekuExtension);

        // OID for Client Authentication: 1.3.6.1.5.5.7.3.2
        var clientAuthOid = ekuExtension.EnhancedKeyUsages
            .Cast<Oid>()
            .FirstOrDefault(o => o.Value == "1.3.6.1.5.5.7.3.2");

        Assert.NotNull(clientAuthOid);
    }

    [Fact]
    public async Task IssueCertificateAsync_CertificateIsNotCa()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        var certPem = result.CertificatePem!;
        var cert = X509Certificate2.CreateFromPem(certPem);

        var bcExtension = cert.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .FirstOrDefault();

        Assert.NotNull(bcExtension);
        Assert.False(bcExtension.CertificateAuthority);
    }

    [Fact]
    public async Task IssueCertificateAsync_Pkcs12CanBeLoaded()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert - PKCS#12 should be loadable with the provided password
        var pfxBytes = Convert.FromBase64String(result.Pkcs12Base64!);
        var cert = new X509Certificate2(pfxBytes, result.Pkcs12Password, X509KeyStorageFlags.Exportable);

        Assert.True(cert.HasPrivateKey);
        Assert.NotNull(cert.GetRSAPrivateKey());
    }

    [Fact]
    public async Task IssueCertificateAsync_ThumbprintIsSha256()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert - Thumbprint should be 64 hex characters (32 bytes = SHA-256)
        Assert.NotNull(result.Thumbprint);
        Assert.Equal(64, result.Thumbprint.Length);
        Assert.True(result.Thumbprint.All(c => "0123456789abcdef".Contains(c)));

        // Verify it matches actual SHA-256 hash of certificate
        var cert = X509Certificate2.CreateFromPem(result.CertificatePem!);
        var expectedThumbprint = Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant();
        Assert.Equal(expectedThumbprint, result.Thumbprint);
    }

    [Fact]
    public async Task IssueCertificateAsync_MultipleCertificatesHaveUniqueSerialNumbers()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result1 = await service.IssueCertificateAsync(Guid.NewGuid());
        var result2 = await service.IssueCertificateAsync(Guid.NewGuid());
        var result3 = await service.IssueCertificateAsync(Guid.NewGuid());

        // Assert
        var serials = new[] { result1.SerialNumber, result2.SerialNumber, result3.SerialNumber };
        Assert.Equal(3, serials.Distinct().Count());
    }

    [Fact]
    public async Task RenewCertificateAsync_GeneratesNewCertificate()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        var originalResult = await service.IssueCertificateAsync(nodeId);

        // Act
        var renewedResult = await service.RenewCertificateAsync(nodeId, originalResult.Thumbprint!);

        // Assert
        Assert.True(renewedResult.Success);
        Assert.NotEqual(originalResult.Thumbprint, renewedResult.Thumbprint);
        Assert.NotEqual(originalResult.SerialNumber, renewedResult.SerialNumber);
    }

    [Fact]
    public async Task RenewCertificateAsync_NewCertificateHasSameNodeId()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        var originalResult = await service.IssueCertificateAsync(nodeId);

        // Act
        var renewedResult = await service.RenewCertificateAsync(nodeId, originalResult.Thumbprint!);

        // Assert
        var cert = X509Certificate2.CreateFromPem(renewedResult.CertificatePem!);
        Assert.Contains($"CN={nodeId}", cert.Subject);
    }

    [Fact]
    public async Task GetCaCertificatePemAsync_ReturnsValidPem()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var caPem = await service.GetCaCertificatePemAsync();

        // Assert
        Assert.NotNull(caPem);
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", caPem);
        Assert.Contains("-----END CERTIFICATE-----", caPem);

        // Should be loadable
        var caCert = X509Certificate2.CreateFromPem(caPem);
        Assert.Contains("Meridian Console Agent CA", caCert.Subject);
    }

    [Fact]
    public async Task GetCaCertificatePemAsync_CaIsSelfSigned()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var caPem = await service.GetCaCertificatePemAsync();
        var caCert = X509Certificate2.CreateFromPem(caPem);

        // Assert - Subject and Issuer should be the same for self-signed
        Assert.Equal(caCert.Subject, caCert.Issuer);
    }

    [Fact]
    public async Task GetCaCertificatePemAsync_CaHasCaBasicConstraint()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var caPem = await service.GetCaCertificatePemAsync();
        var caCert = X509Certificate2.CreateFromPem(caPem);

        // Assert
        var bcExtension = caCert.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .FirstOrDefault();

        Assert.NotNull(bcExtension);
        Assert.True(bcExtension.CertificateAuthority);
    }

    [Fact]
    public async Task ValidateCertificateAsync_ReturnsTrueForIssuedCertificate()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        var result = await service.IssueCertificateAsync(nodeId);

        // Act
        var isValid = await service.ValidateCertificateAsync(result.CertificatePem!);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateCertificateAsync_ReturnsFalseForForeignCertificate()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();

        // Create a self-signed certificate not issued by our CA
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Foreign", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var foreignCert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var foreignPem = foreignCert.ExportCertificatePem();

        // Act
        var isValid = await service.ValidateCertificateAsync(foreignPem);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task IssueCertificateAsync_CertificateChainIsValid()
    {
        // Arrange
        var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Get CA certificate
        var caPem = await service.GetCaCertificatePemAsync();
        var caCert = X509Certificate2.CreateFromPem(caPem);
        var clientCert = X509Certificate2.CreateFromPem(result.CertificatePem!);

        // Assert - Build chain
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.ExtraStore.Add(caCert);

        var chainBuilds = chain.Build(clientCert);
        Assert.True(chainBuilds);
    }
}
