using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dhadgar.Nodes.Tests;

/// <summary>
/// Tests for issuing agent certificates bound to agent-generated public keys
/// (the private key never exists on the control plane).
/// </summary>
public sealed class CertificateAuthorityPublicKeyIssuanceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly IOptions<NodesOptions> _options;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly NodesDbContext _dbContext;

    public CertificateAuthorityPublicKeyIssuanceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ca-pk-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
        _options = Options.Create(new NodesOptions
        {
            CaStoragePath = _tempDirectory,
            CaKeySize = 2048,
            ClientKeySize = 2048,
            CaValidityYears = 10,
            CertificateValidityDays = 90
        });
        _dbContext = new NodesDbContext(new DbContextOptionsBuilder<NodesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private CertificateAuthorityService CreateService() => new(
        new LocalFileCaStorageProvider(_options, NullLogger<LocalFileCaStorageProvider>.Instance),
        _options,
        _timeProvider,
        NullLogger<CertificateAuthorityService>.Instance,
        _dbContext);

    [Fact]
    public async Task EcdsaP384Key_IsSignedWithoutReturningAnyPrivateKey()
    {
        using var service = CreateService();
        using var agentKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var nodeId = Guid.NewGuid();

        var result = await service.IssueCertificateForPublicKeyAsync(
            nodeId, Convert.ToBase64String(agentKey.ExportSubjectPublicKeyInfo()));

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Pkcs12Base64);
        Assert.Null(result.Pkcs12Password);

        using var cert = X509Certificate2.CreateFromPem(result.CertificatePem!);
        Assert.False(cert.HasPrivateKey);

        // Bound to exactly the agent's key
        using var certKey = cert.GetECDsaPublicKey();
        Assert.NotNull(certKey);
        Assert.Equal(agentKey.ExportSubjectPublicKeyInfo(), certKey.ExportSubjectPublicKeyInfo());

        // Same profile as server-generated certs
        Assert.Contains($"CN={nodeId}", cert.Subject, StringComparison.Ordinal);
        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), o => o.Value == "1.3.6.1.5.5.7.3.2");
        var ku = cert.Extensions.OfType<X509KeyUsageExtension>().Single();
        Assert.Equal(X509KeyUsageFlags.DigitalSignature, ku.KeyUsages);
        Assert.False(cert.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority);
        Assert.Equal(TimeSpan.FromDays(90), result.NotAfter!.Value - result.NotBefore!.Value);

        var san = cert.Extensions.Single(e => e.Oid?.Value == "2.5.29.17");
        Assert.Contains($"spiffe://meridianconsole.com/nodes/{nodeId}", san.Format(false), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IssuedCertificate_ChainsToTheAgentCa()
    {
        using var service = CreateService();
        using var agentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var result = await service.IssueCertificateForPublicKeyAsync(
            Guid.NewGuid(), Convert.ToBase64String(agentKey.ExportSubjectPublicKeyInfo()));
        Assert.True(result.Success, result.Error);

        using var cert = X509Certificate2.CreateFromPem(result.CertificatePem!);
        using var ca = X509Certificate2.CreateFromPem(await service.GetCaCertificatePemAsync());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);

        Assert.True(chain.Build(cert), string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation)));
    }

    [Fact]
    public async Task Rsa2048Key_IsAccepted()
    {
        using var service = CreateService();
        using var rsa = RSA.Create(2048);

        var result = await service.IssueCertificateForPublicKeyAsync(
            Guid.NewGuid(), Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()));

        Assert.True(result.Success, result.Error);
    }

    public static TheoryData<string> RejectedKeys()
    {
        using var rsa1024 = RSA.Create(1024);
        using var p521 = ECDsa.Create(ECCurve.NamedCurves.nistP521);
        using var good = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        return new TheoryData<string>
        {
            "",
            "   ",
            "not base64!!",
            Convert.ToBase64String("definitely not an SPKI"u8.ToArray()),
            Convert.ToBase64String(rsa1024.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(p521.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String([.. good.ExportSubjectPublicKeyInfo(), 0x00]) // trailing data
        };
    }

    [Theory]
    [MemberData(nameof(RejectedKeys))]
    public async Task WeakMalformedOrUnsupportedKeys_AreRejected(string spkiBase64)
    {
        using var service = CreateService();

        var result = await service.IssueCertificateForPublicKeyAsync(Guid.NewGuid(), spkiBase64);

        Assert.False(result.Success);
        Assert.Equal("invalid_public_key", result.Error);
    }
}
