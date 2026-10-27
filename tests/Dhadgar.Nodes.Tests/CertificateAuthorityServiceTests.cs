using System.Formats.Asn1;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly List<ServiceProvider> _providers = [];

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
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

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

    private ServiceProvider CreateDbServices(Action<IServiceCollection>? configure = null)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<NodesDbContext>(o => o
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private CertificateAuthorityService CreateService(
        ICaStorageProvider? storageProvider = null,
        ServiceProvider? dbServices = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        storageProvider ??= new LocalFileCaStorageProvider(
            _optionsWrapper,
            NullLogger<LocalFileCaStorageProvider>.Instance);

        return new CertificateAuthorityService(
            storageProvider,
            _optionsWrapper,
            _timeProvider,
            NullLogger<CertificateAuthorityService>.Instance,
            scopeFactory ?? (dbServices ?? CreateDbServices()).GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task InitializeAsync_CreatesNewCa_WhenNoExistingCa()
    {
        // Arrange
        using var service = CreateService();

        // Act
        await service.InitializeAsync();

        // Assert - CA certificate should be created
        var caPem = await service.GetCaCertificatePemAsync();
        Assert.NotNull(caPem);
        Assert.Contains("BEGIN CERTIFICATE", caPem, StringComparison.Ordinal);
        Assert.Contains("END CERTIFICATE", caPem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingCa_WhenCaExists()
    {
        // Arrange - Initialize once to create CA
        using var service1 = CreateService();
        await service1.InitializeAsync();
        var caPem1 = await service1.GetCaCertificatePemAsync();

        // Act - Create new service instance and initialize
        using var service2 = CreateService();
        await service2.InitializeAsync();
        var caPem2 = await service2.GetCaCertificatePemAsync();

        // Assert - Should load same CA
        Assert.Equal(caPem1, caPem2);
    }

    [Fact]
    public async Task IssueCertificateAsync_GeneratesValidCertificate()
    {
        // Arrange
        using var service = CreateService();
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
        using var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        var certPem = result.CertificatePem!;
        using var cert = X509Certificate2.CreateFromPem(certPem);

        Assert.Contains($"CN={nodeId}", cert.Subject, StringComparison.Ordinal);
        Assert.Contains("O=MeridianConsole", cert.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IssueCertificateAsync_CertificateHasSpiffeId()
    {
        // Arrange
        using var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        var certPem = result.CertificatePem!;
        using var cert = X509Certificate2.CreateFromPem(certPem);

        // Check Subject Alternative Name extension contains SPIFFE ID
        var sanExtension = cert.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .FirstOrDefault();

        Assert.NotNull(sanExtension);

        // The SAN should contain the SPIFFE URI
        var sanString = sanExtension.Format(multiLine: true);
        Assert.Contains($"spiffe://meridianconsole.com/nodes/{nodeId}", sanString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IssueCertificateAsync_CertificateHasClientAuthEku()
    {
        // Arrange
        using var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        var certPem = result.CertificatePem!;
        using var cert = X509Certificate2.CreateFromPem(certPem);

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
        using var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert
        var certPem = result.CertificatePem!;
        using var cert = X509Certificate2.CreateFromPem(certPem);

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
        using var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert - PKCS#12 should be loadable with the provided password
        var pfxBytes = Convert.FromBase64String(result.Pkcs12Base64!);
        using var cert = new X509Certificate2(pfxBytes, result.Pkcs12Password, X509KeyStorageFlags.Exportable);

        Assert.True(cert.HasPrivateKey);
        Assert.NotNull(cert.GetRSAPrivateKey());
    }

    [Fact]
    public async Task IssueCertificateAsync_ThumbprintIsSha256()
    {
        // Arrange
        using var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Assert - Thumbprint should be 64 hex characters (32 bytes = SHA-256)
        Assert.NotNull(result.Thumbprint);
        Assert.Equal(64, result.Thumbprint.Length);
        Assert.True(result.Thumbprint.All(c => "0123456789abcdef".Contains(c, StringComparison.Ordinal)));

        // Verify it matches actual SHA-256 hash of certificate
        using var cert = X509Certificate2.CreateFromPem(result.CertificatePem!);
        var expectedThumbprint = Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant();
        Assert.Equal(expectedThumbprint, result.Thumbprint);
    }

    [Fact]
    public async Task IssueCertificateAsync_MultipleCertificatesHaveUniqueSerialNumbers()
    {
        // Arrange
        using var service = CreateService();
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
    public async Task GetCaCertificatePemAsync_ReturnsValidPem()
    {
        // Arrange
        using var service = CreateService();
        await service.InitializeAsync();

        // Act
        var caPem = await service.GetCaCertificatePemAsync();

        // Assert
        Assert.NotNull(caPem);
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", caPem, StringComparison.Ordinal);
        Assert.Contains("-----END CERTIFICATE-----", caPem, StringComparison.Ordinal);

        // Should be loadable
        using var caCert = X509Certificate2.CreateFromPem(caPem);
        Assert.Contains("Meridian Console Agent CA", caCert.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCaCertificatePemAsync_CaIsSelfSigned()
    {
        // Arrange
        using var service = CreateService();
        await service.InitializeAsync();

        // Act
        var caPem = await service.GetCaCertificatePemAsync();
        using var caCert = X509Certificate2.CreateFromPem(caPem);

        // Assert - Subject and Issuer should be the same for self-signed
        Assert.Equal(caCert.Subject, caCert.Issuer);
    }

    [Fact]
    public async Task GetCaCertificatePemAsync_CaHasCaBasicConstraint()
    {
        // Arrange
        using var service = CreateService();
        await service.InitializeAsync();

        // Act
        var caPem = await service.GetCaCertificatePemAsync();
        using var caCert = X509Certificate2.CreateFromPem(caPem);

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
        using var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        var result = await service.IssueCertificateAsync(nodeId);

        // Act
        var trust = await service.ValidateCertificateAsync(result.CertificatePem!);

        // Assert
        Assert.Equal(CertificateTrustStatus.Trusted, trust);
    }

    [Fact]
    public async Task ValidateCertificateAsync_ReturnsFalseForForeignCertificate()
    {
        // Arrange
        using var service = CreateService();
        await service.InitializeAsync();

        // Create a self-signed certificate not issued by our CA
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Foreign", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var foreignCert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var foreignPem = foreignCert.ExportCertificatePem();

        // Act
        var trust = await service.ValidateCertificateAsync(foreignPem);

        // Assert
        Assert.Equal(CertificateTrustStatus.Untrusted, trust);
    }

    [Fact]
    public async Task IssueCertificateAsync_CertificateChainIsValid()
    {
        // Arrange
        using var service = CreateService();
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Act
        var result = await service.IssueCertificateAsync(nodeId);

        // Get CA certificate
        var caPem = await service.GetCaCertificatePemAsync();
        using var caCert = X509Certificate2.CreateFromPem(caPem);
        using var clientCert = X509Certificate2.CreateFromPem(result.CertificatePem!);

        // Assert - Build chain
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        // Ignore time validity since tests use FakeTimeProvider (dates may be in the past)
        chain.ChainPolicy.VerificationFlags =
            X509VerificationFlags.AllowUnknownCertificateAuthority |
            X509VerificationFlags.IgnoreNotTimeValid;
        chain.ChainPolicy.ExtraStore.Add(caCert);

        var chainBuilds = chain.Build(clientCert);
        Assert.True(chainBuilds);
    }

    [Fact]
    public async Task ValidateCertificateAsync_ReturnsFalseForRevokedCertificate()
    {
        // Arrange
        var dbServices = CreateDbServices();
        using var service = CreateService(dbServices: dbServices);
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Issue a certificate
        var result = await service.IssueCertificateAsync(nodeId);
        Assert.True(result.Success);

        // Mark the certificate as revoked in the database
        var agentCert = new AgentCertificate
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Thumbprint = result.Thumbprint!,
            SerialNumber = result.SerialNumber!,
            NotBefore = result.NotBefore!.Value,
            NotAfter = result.NotAfter!.Value,
            IssuedAt = DateTime.UtcNow,
            IsRevoked = true,
            RevokedAt = DateTime.UtcNow,
            RevocationReason = "Test revocation"
        };
        using (var scope = dbServices.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            dbContext.AgentCertificates.Add(agentCert);
            await dbContext.SaveChangesAsync();
        }

        // Act
        var trust = await service.ValidateCertificateAsync(result.CertificatePem!);

        // Assert
        Assert.Equal(CertificateTrustStatus.Untrusted, trust);
    }

    [Fact]
    public async Task ValidateCertificate_RevokedInGrace_IsTrusted()
    {
        // Arrange - superseded by a renewal, still inside its grace window (D-S212)
        var dbServices = CreateDbServices();
        using var service = CreateService(dbServices: dbServices);
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();
        var result = await service.IssueCertificateAsync(nodeId);
        Assert.True(result.Success);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await AddRevokedRowAsync(dbServices, nodeId, result, now.AddMinutes(-10), now.AddMinutes(20));

        // Act
        var trust = await service.ValidateCertificateAsync(result.CertificatePem!);

        // Assert
        Assert.Equal(CertificateTrustStatus.Trusted, trust);
    }

    [Fact]
    public async Task ValidateCertificate_RevokedPastGrace_IsUntrusted()
    {
        // Arrange - the grace window closed one minute ago
        var dbServices = CreateDbServices();
        using var service = CreateService(dbServices: dbServices);
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();
        var result = await service.IssueCertificateAsync(nodeId);
        Assert.True(result.Success);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await AddRevokedRowAsync(dbServices, nodeId, result, now.AddMinutes(-70), now.AddMinutes(-1));

        // Act
        var trust = await service.ValidateCertificateAsync(result.CertificatePem!);

        // Assert
        Assert.Equal(CertificateTrustStatus.Untrusted, trust);
    }

    private static async Task AddRevokedRowAsync(
        ServiceProvider dbServices,
        Guid nodeId,
        CertificateIssuanceResult result,
        DateTime revokedAt,
        DateTime graceUntil)
    {
        using var scope = dbServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        dbContext.AgentCertificates.Add(new AgentCertificate
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Thumbprint = result.Thumbprint!,
            SerialNumber = result.SerialNumber!,
            NotBefore = result.NotBefore!.Value,
            NotAfter = result.NotAfter!.Value,
            IssuedAt = revokedAt,
            IsRevoked = true,
            RevokedAt = revokedAt,
            RevocationReason = "Renewed - superseded by new certificate",
            GraceUntil = graceUntil
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ValidateCertificateAsync_ReturnsTrueForNonRevokedCertificateInDatabase()
    {
        // Arrange
        var dbServices = CreateDbServices();
        using var service = CreateService(dbServices: dbServices);
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();

        // Issue a certificate
        var result = await service.IssueCertificateAsync(nodeId);
        Assert.True(result.Success);

        // Add the certificate to the database but not revoked
        var agentCert = new AgentCertificate
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Thumbprint = result.Thumbprint!,
            SerialNumber = result.SerialNumber!,
            NotBefore = result.NotBefore!.Value,
            NotAfter = result.NotAfter!.Value,
            IssuedAt = DateTime.UtcNow,
            IsRevoked = false
        };
        using (var scope = dbServices.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            dbContext.AgentCertificates.Add(agentCert);
            await dbContext.SaveChangesAsync();
        }

        // Act
        var trust = await service.ValidateCertificateAsync(result.CertificatePem!);

        // Assert
        Assert.Equal(CertificateTrustStatus.Trusted, trust);
    }

    private static string CreateForeignPem()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Foreign", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var foreignCert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        return foreignCert.ExportCertificatePem();
    }

    [Fact]
    public async Task ValidateCertificateAsync_ConcurrentCallsOnOneInstance_AllTrusted()
    {
        var dbServices = CreateDbServices();
        var counting = new CountingScopeFactory(dbServices.GetRequiredService<IServiceScopeFactory>());
        using var service = CreateService(scopeFactory: counting);
        await service.InitializeAsync();
        var nodeId = Guid.NewGuid();
        var result = await service.IssueCertificateAsync(nodeId);
        Assert.True(result.Success);

        using (var scope = dbServices.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            dbContext.AgentCertificates.Add(new AgentCertificate
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                Thumbprint = result.Thumbprint!,
                SerialNumber = result.SerialNumber!,
                NotBefore = result.NotBefore!.Value,
                NotAfter = result.NotAfter!.Value,
                IssuedAt = DateTime.UtcNow,
                IsRevoked = false
            });
            await dbContext.SaveChangesAsync();
        }

        var pem = result.CertificatePem!;
        var before = counting.Count;
        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => service.ValidateCertificateAsync(pem))));

        Assert.All(results, r => Assert.Equal(CertificateTrustStatus.Trusted, r));
        // Each validation opens its own scope: one per call, none shared or reused.
        Assert.Equal(before + 64, counting.Count);
    }

    [Fact]
    public async Task ValidateCertificateAsync_WhenRevocationLookupFails_ReturnsUnavailable()
    {
        var dbServices = CreateDbServices(s =>
            s.AddScoped<NodesDbContext>(_ => throw new InvalidOperationException("database unavailable")));
        using var service = CreateService(dbServices: dbServices);
        await service.InitializeAsync();
        var result = await service.IssueCertificateAsync(Guid.NewGuid());
        Assert.True(result.Success);

        var trust = await service.ValidateCertificateAsync(result.CertificatePem!);

        Assert.Equal(CertificateTrustStatus.Unavailable, trust);
    }

    [Fact]
    public async Task ValidateCertificateAsync_ForeignCertificate_DoesNotQueryDatabase()
    {
        var dbServices = CreateDbServices(s =>
            s.AddScoped<NodesDbContext>(_ => throw new InvalidOperationException("database unavailable")));
        using var service = CreateService(dbServices: dbServices);
        await service.InitializeAsync();

        var trust = await service.ValidateCertificateAsync(CreateForeignPem());

        Assert.Equal(CertificateTrustStatus.Untrusted, trust);
    }

    [Fact]
    public async Task ValidateCertificateAsync_ForeignCertificateWithAiaUrl_DoesNotFetchIssuer()
    {
        // The client controls the certificate, so its AIA URL must never be fetched by the chain engine.
        var port = GetFreeTcpPort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        var requestCount = 0;
        var accepting = Task.Run(async () =>
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (!listener.IsListening)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                Interlocked.Increment(ref requestCount);
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        });

        try
        {
            using var service = CreateService();
            await service.InitializeAsync();

            var trust = await service.ValidateCertificateAsync(CreateForeignPemWithAiaUrl(prefix + "ca.cer"));

            Assert.Equal(CertificateTrustStatus.Untrusted, trust);
        }
        finally
        {
            listener.Stop();
            await accepting;
        }

        Assert.Equal(0, Volatile.Read(ref requestCount));
    }

    private static int GetFreeTcpPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static string CreateForeignPemWithAiaUrl(string caIssuersUrl)
    {
        var now = DateTimeOffset.UtcNow;

        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=Foreign Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            critical: true));
        using var caCert = caRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=Foreign Agent", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509Extension(
            "1.3.6.1.5.5.7.1.1", // id-pe-authorityInfoAccess
            BuildCaIssuersAccessExtension(caIssuersUrl),
            critical: false));

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        using var leaf = leafRequest.Create(caCert, now.AddMinutes(-5), now.AddDays(1), serial);
        return leaf.ExportCertificatePem();
    }

    private static byte[] BuildCaIssuersAccessExtension(string url)
    {
        // AuthorityInfoAccessSyntax ::= SEQUENCE OF AccessDescription
        // AccessDescription ::= SEQUENCE { accessMethod OID, accessLocation GeneralName (uniformResourceIdentifier [6]) }
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier("1.3.6.1.5.5.7.48.2"); // id-ad-caIssuers
            writer.WriteCharacterString(
                UniversalTagNumber.IA5String,
                url,
                new Asn1Tag(TagClass.ContextSpecific, 6));
        }

        return writer.Encode();
    }

    /// <summary>Counts how many scopes the service opens, to prove one scope per lookup.</summary>
    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _count);
            return inner.CreateScope();
        }
    }
}
