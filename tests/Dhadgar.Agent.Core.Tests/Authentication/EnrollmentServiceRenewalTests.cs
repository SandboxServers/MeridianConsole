using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Contracts.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Authentication;

public sealed class EnrollmentServiceRenewalTests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("6f1c2b3a-4d5e-4f60-8a71-92b3c4d5e6f7");

    private readonly TestAgentCa _ca = new();
    private readonly ECDsa _currentKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);

    public void Dispose()
    {
        _currentKey.Dispose();
        _ca.Dispose();
    }

    [Fact]
    public async Task RenewalRequestBody_IsExactlyCamelCasePublicKey()
    {
        var handler = new ScriptedHttpHandler((_, body) =>
            Renewed(_ca.Issue(AgentCertificateTestJson.ReadPublicKey(body), Spiffe(NodeId))));
        var sut = BuildSut(handler, out var store, out _);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsSuccess, result.Error);
        var body = Assert.IsType<string>(handler.LastRequestBody);
        Assert.StartsWith("{\"publicKey\":\"", body, StringComparison.Ordinal);
        Assert.EndsWith("\"}", body, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        Assert.Single(document.RootElement.EnumerateObject());
        Assert.Equal(1, store.StoreCertificateCount);
    }

    [Fact]
    public async Task PemResponse_IsDecodedAndStored()
    {
        X509Certificate2? issued = null;
        var handler = new ScriptedHttpHandler((_, body) =>
        {
            issued = _ca.Issue(AgentCertificateTestJson.ReadPublicKey(body), Spiffe(NodeId));
            return Renewed(issued);
        });
        var sut = BuildSut(handler, out var store, out var provider);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, store.StoreCertificateCount);
        Assert.Equal(issued!.RawData, store.StoredCertificateDer);
        Assert.Equal(1, store.StoreCaCertificateCount);
        Assert.Equal(issued.NotAfter, result.Value!.NewExpiry);
        issued.Dispose();
        Assert.Equal(1, provider.InvalidateCount);
    }

    [Fact]
    public async Task StoredKeyBytes_LoadWithImportPkcs8PrivateKey_AndMatchTheCertificate()
    {
        X509Certificate2? issued = null;
        var handler = new ScriptedHttpHandler((_, body) =>
        {
            issued = _ca.Issue(AgentCertificateTestJson.ReadPublicKey(body), Spiffe(NodeId));
            return Renewed(issued);
        });
        var sut = BuildSut(handler, out var store, out _);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsSuccess, result.Error);
        var storedKey = Assert.IsType<byte[]>(store.StoredPrivateKey);
        using var agentKey = ECDsa.Create();
        agentKey.ImportPkcs8PrivateKey(storedKey, out var bytesRead);
        Assert.Equal(storedKey.Length, bytesRead);
        Assert.Equal(
            issued!.GetECDsaPublicKey()!.ExportSubjectPublicKeyInfo(),
            agentKey.ExportSubjectPublicKeyInfo());
        issued!.Dispose();
    }

    [Fact]
    public async Task KeyMismatchResponse_FailsWithoutStoring()
    {
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var handler = new ScriptedHttpHandler((_, _) =>
            Renewed(_ca.Issue(otherKey.ExportSubjectPublicKeyInfo(), Spiffe(NodeId))));
        var sut = BuildSut(handler, out var store, out var provider);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("[Renewal.KeyMismatch]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
        Assert.Equal(0, provider.InvalidateCount);
    }

    [Fact]
    public async Task WrongSpiffeIdentity_FailsWithoutStoring()
    {
        var otherNode = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var handler = new ScriptedHttpHandler((_, body) =>
            Renewed(_ca.Issue(AgentCertificateTestJson.ReadPublicKey(body), Spiffe(otherNode))));
        var sut = BuildSut(handler, out var store, out var provider);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("[Renewal.WrongIdentity]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
        Assert.Equal(0, store.StoreCaCertificateCount);
        Assert.Equal(0, provider.InvalidateCount);
    }

    [Fact]
    public async Task UnauthorizedProblemResponse_ReportsThumbprintMismatch()
    {
        var handler = new ScriptedHttpHandler((_, _) => AgentCertificateTestJson.Json(
            HttpStatusCode.Unauthorized,
            "{\"type\":\"about:blank\",\"title\":\"Unauthorized\",\"status\":401,\"errorCode\":\"thumbprint_mismatch\"}",
            "application/problem+json"));
        var sut = BuildSut(handler, out var store, out _);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("thumbprint_mismatch", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
    }

    [Fact]
    public async Task OversizedResponse_Fails()
    {
        var handler = new ScriptedHttpHandler((_, _) =>
        {
            var content = new ByteArrayContent(new byte[2 * 1024 * 1024]);
            content.Headers.ContentLength = 2 * 1024 * 1024;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var sut = BuildSut(handler, out var store, out var provider);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("[Renewal.ResponseTooLarge]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
        Assert.Equal(0, provider.InvalidateCount);
    }

    [Fact]
    public async Task AfterSuccess_TheHandshakeProviderIsInvalidated()
    {
        var handler = new ScriptedHttpHandler((_, body) =>
            Renewed(_ca.Issue(AgentCertificateTestJson.ReadPublicKey(body), Spiffe(NodeId))));
        var sut = BuildSut(handler, out var store, out var provider);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, provider.InvalidateCount);
        Assert.Equal(1, store.StoreCertificateCount);
    }

    /// <summary>Builds the success response for a leaf certificate, with a PEM CA.</summary>
    private HttpResponseMessage Renewed(X509Certificate2 leaf)
    {
        var response = new RenewCertificateResponse(
            CertificateThumbprint: leaf.Thumbprint,
            Certificate: TestAgentCa.ToPem(leaf),
            CaCertificate: _ca.CertificatePem,
            NotBefore: leaf.NotBefore,
            NotAfter: leaf.NotAfter);

        return AgentCertificateTestJson.Json(
            HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task MalformedSanResponse_FailsWithoutStoring()
    {
        // 30 05 86 09 41: a GeneralName [6] claiming 9 bytes with only 1 present. Loads as a certificate,
        // but reading the SAN throws AsnContentException, which must become a wrong-identity failure.
        var malformedSan = new byte[] { 0x30, 0x05, 0x86, 0x09, 0x41 };
        var handler = new ScriptedHttpHandler((_, body) =>
            Renewed(_ca.Issue(
                AgentCertificateTestJson.ReadPublicKey(body),
                Spiffe(NodeId),
                rawSanExtension: malformedSan)));
        var sut = BuildSut(handler, out var store, out var provider);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("[Renewal.WrongIdentity]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
        Assert.Equal(0, provider.InvalidateCount);
    }

    [Fact]
    public async Task ExpiredResponse_FailsWithoutStoring()
    {
        var handler = new ScriptedHttpHandler((_, body) =>
            Renewed(_ca.Issue(
                AgentCertificateTestJson.ReadPublicKey(body),
                Spiffe(NodeId),
                notBefore: DateTimeOffset.UtcNow.AddDays(-2),
                notAfter: DateTimeOffset.UtcNow.AddDays(-1))));
        var sut = BuildSut(handler, out var store, out var provider);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("[Renewal.InvalidValidity]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
        Assert.Equal(0, provider.InvalidateCount);
    }

    [Fact]
    public async Task NotYetValidResponse_FailsWithoutStoring()
    {
        var handler = new ScriptedHttpHandler((_, body) =>
            Renewed(_ca.Issue(
                AgentCertificateTestJson.ReadPublicKey(body),
                Spiffe(NodeId),
                notBefore: DateTimeOffset.UtcNow.AddHours(1),
                notAfter: DateTimeOffset.UtcNow.AddDays(30))));
        var sut = BuildSut(handler, out var store, out var provider);

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("[Renewal.InvalidValidity]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
        Assert.Equal(0, provider.InvalidateCount);
    }

    [Fact]
    public async Task StoreThrows_StillInvalidatesTheProvider()
    {
        var handler = new ScriptedHttpHandler((_, body) =>
            Renewed(_ca.Issue(AgentCertificateTestJson.ReadPublicKey(body), Spiffe(NodeId))));
        var sut = BuildSut(handler, out var store, out var provider);
        store.StoreException = new CryptographicException("store failed part-way");

        var result = await sut.RenewCertificateAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(1, provider.InvalidateCount);
    }

    private static string Spiffe(Guid nodeId) => AgentCertificateProfile.SpiffeId(nodeId);

    private EnrollmentService BuildSut(
        ScriptedHttpHandler handler,
        out RecordingCertificateStore store,
        out RecordingClientCertificateProvider provider)
    {
        // The agent already holds a certificate for its own node; renewal replaces it.
        using var current = _ca.Issue(_currentKey.ExportSubjectPublicKeyInfo(), Spiffe(NodeId));
        store = new RecordingCertificateStore(current);
        provider = new RecordingClientCertificateProvider();

        // Renewal reads the identity from the provider, which the enrolled agent has already set.
        var identity = new AgentIdentityProvider();
        identity.Set(new AgentIdentity(NodeId, Guid.NewGuid(), DateTimeOffset.UtcNow));

        return new EnrollmentService(
            store,
            new ScriptedHttpClientFactory(handler),
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new FixedHardwareInventoryCollector(),
            clientCertificateProvider: provider,
            identityProvider: identity);
    }
}
