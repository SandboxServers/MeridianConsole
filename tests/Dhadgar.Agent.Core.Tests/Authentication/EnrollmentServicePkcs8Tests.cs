using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Contracts.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Authentication;

/// <summary>
/// Pins the enrollment handoff to PKCS#8: the key the store receives must be the format every
/// store and the mTLS provider read, and the provider must be reset once new material lands.
/// </summary>
public sealed class EnrollmentServicePkcs8Tests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("0b7e4c1d-2a3f-4b5c-9d6e-7f8091a2b3c4");
    private static readonly Guid OrganizationId = Guid.Parse("c4d3b2a1-0f9e-4d8c-8b7a-6f5e4d3c2b1a");

    private readonly TestAgentCa _ca = new();

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Enrollment_StoresPkcs8Key_ThatLoadsAndMatchesTheCertificate()
    {
        var handler = new ScriptedHttpHandler((_, body) => Enrolled(body));
        var store = new RecordingCertificateStore();
        var provider = new RecordingClientCertificateProvider();
        var sut = new EnrollmentService(
            store,
            new ScriptedHttpClientFactory(handler),
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new FixedHardwareInventoryCollector(),
            clientCertificateProvider: provider);

        var result = await sut.EnrollAsync("enrollment-token-for-test");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, store.StoreCertificateCount);
        var storedKey = Assert.IsType<byte[]>(store.StoredPrivateKey);

        // PKCS#8 PrivateKeyInfo, not SEC1 ECPrivateKey: the import must succeed in this format
        using var agentKey = ECDsa.Create();
        agentKey.ImportPkcs8PrivateKey(storedKey, out _);
        Assert.Equal(
            ExpectedPublicKey(handler.LastRequestBody!),
            agentKey.ExportSubjectPublicKeyInfo());

        Assert.Equal(1, provider.InvalidateCount);
    }

    [Fact]
    public async Task Enrollment_StoredKey_IsNotSec1()
    {
        var handler = new ScriptedHttpHandler((_, body) => Enrolled(body));
        var store = new RecordingCertificateStore();
        var sut = new EnrollmentService(
            store,
            new ScriptedHttpClientFactory(handler),
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new FixedHardwareInventoryCollector(),
            clientCertificateProvider: new RecordingClientCertificateProvider());

        await sut.EnrollAsync("enrollment-token-for-test");

        // The stored bytes are PKCS#8, so the SEC1 importer (ECPrivateKey) must reject them
        var storedKey = Assert.IsType<byte[]>(store.StoredPrivateKey);
        using var sec1Probe = ECDsa.Create();
        Assert.ThrowsAny<CryptographicException>(() => sec1Probe.ImportECPrivateKey(storedKey, out _));
    }

    /// <summary>The enrollment request's public key, decoded from its base64 SubjectPublicKeyInfo.</summary>
    private static byte[] ExpectedPublicKey(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        return Convert.FromBase64String(document.RootElement.GetProperty("publicKey").GetString()!);
    }

    /// <summary>Signs the enrollment's public key so the certificate passes the key-binding check.</summary>
    private HttpResponseMessage Enrolled(string requestBody)
    {
        using var leaf = _ca.Issue(ExpectedPublicKey(requestBody), AgentCertificateProfile.SpiffeId(NodeId));
        var response = new EnrollNodeResponse(
            NodeId: NodeId,
            OrganizationId: OrganizationId,
            CertificateThumbprint: leaf.Thumbprint,
            Certificate: TestAgentCa.ToPem(leaf),
            CaCertificate: _ca.CertificatePem,
            NotBefore: leaf.NotBefore,
            NotAfter: leaf.NotAfter);

        return AgentCertificateTestJson.Json(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
