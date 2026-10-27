using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Identity;
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
    private static readonly Guid OtherNodeId = Guid.Parse("66666666-7777-8888-9999-000000000000");
    private static readonly Guid OrganizationId = Guid.Parse("c4d3b2a1-0f9e-4d8c-8b7a-6f5e4d3c2b1a");
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TestAgentCa _ca = new();

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Enrollment_StoresPkcs8Key_ThatLoadsAndMatchesTheCertificate()
    {
        using var handler = new ScriptedHttpHandler((_, body) => Enrolled(body));
        var store = new RecordingCertificateStore();
        var provider = new RecordingClientCertificateProvider();
        var sut = new EnrollmentService(
            store,
            new ScriptedHttpClientFactory(handler),
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new FixedHardwareInventoryCollector(),
            clientCertificateProvider: provider,
            identityProvider: new AgentIdentityProvider());

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
        using var handler = new ScriptedHttpHandler((_, body) => Enrolled(body));
        var store = new RecordingCertificateStore();
        var sut = new EnrollmentService(
            store,
            new ScriptedHttpClientFactory(handler),
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new FixedHardwareInventoryCollector(),
            clientCertificateProvider: new RecordingClientCertificateProvider(),
            identityProvider: new AgentIdentityProvider());

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

    [Fact]
    public async Task Enrollment_CertificateForAnotherNode_FailsBeforeStoring()
    {
        using var handler = new ScriptedHttpHandler((_, body) => Enrolled(body, AgentCertificateProfile.SpiffeId(OtherNodeId)));
        var store = new RecordingCertificateStore();
        var sut = new EnrollmentService(
            store,
            new ScriptedHttpClientFactory(handler),
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new FixedHardwareInventoryCollector(),
            clientCertificateProvider: new RecordingClientCertificateProvider(),
            identityProvider: new AgentIdentityProvider());

        var result = await sut.EnrollAsync("enrollment-token-for-test");

        Assert.True(result.IsFailure);
        Assert.Contains("[Enrollment.WrongIdentity]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
    }

    [Fact]
    public async Task Enrollment_ExpiredCertificate_FailsBeforeStoring()
    {
        using var handler = new ScriptedHttpHandler((_, body) => Enrolled(
            body,
            AgentCertificateProfile.SpiffeId(NodeId),
            DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-1)));
        var store = new RecordingCertificateStore();
        var sut = new EnrollmentService(
            store,
            new ScriptedHttpClientFactory(handler),
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new FixedHardwareInventoryCollector(),
            clientCertificateProvider: new RecordingClientCertificateProvider(),
            identityProvider: new AgentIdentityProvider());

        var result = await sut.EnrollAsync("enrollment-token-for-test");

        Assert.True(result.IsFailure);
        Assert.Contains("[Enrollment.InvalidValidity]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
    }

    /// <summary>Signs the enrollment's public key so the certificate passes the key-binding check.</summary>
    private HttpResponseMessage Enrolled(string requestBody) =>
        Enrolled(requestBody, AgentCertificateProfile.SpiffeId(NodeId));

    /// <summary>Signs the enrollment's public key with the given SPIFFE SAN and validity window.</summary>
    private HttpResponseMessage Enrolled(
        string requestBody,
        string spiffeId,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var leaf = _ca.Issue(ExpectedPublicKey(requestBody), spiffeId, notBefore, notAfter);
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
            JsonSerializer.Serialize(response, WebJsonOptions));
    }
}
