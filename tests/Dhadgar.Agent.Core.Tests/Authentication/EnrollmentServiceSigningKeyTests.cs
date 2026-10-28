using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Contracts.Agents;
using Dhadgar.Contracts.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dhadgar.Agent.Core.Tests.Authentication;

/// <summary>
/// Pins the command signing key handoff at enrollment (D-09): a malformed or mismatched key must
/// fail before the certificate is stored, and a valid key must surface on the enrollment result.
/// </summary>
public sealed class EnrollmentServiceSigningKeyTests : IDisposable
{
    private static readonly Guid NodeId = Guid.Parse("0b7e4c1d-2a3f-4b5c-9d6e-7f8091a2b3c4");
    private static readonly Guid OrganizationId = Guid.Parse("c4d3b2a1-0f9e-4d8c-8b7a-6f5e4d3c2b1a");
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TestAgentCa _ca = new();

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Enrollment_MalformedSigningKey_FailsBeforeStoring()
    {
        var garbage = new byte[] { 1, 2, 3, 4 };
        using var handler = new ScriptedHttpHandler((_, body) => Enrolled(
            body,
            Convert.ToBase64String(garbage),
            AgentCommandSigning.ComputeKeyId(garbage)));
        var store = new RecordingCertificateStore();
        var sut = CreateSut(handler, store);

        var result = await sut.EnrollAsync("enrollment-token-for-test");

        Assert.True(result.IsFailure);
        Assert.Contains("[Enrollment.InvalidSigningKey]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
    }

    [Fact]
    public async Task Enrollment_SigningKeyIdMismatch_FailsBeforeStoring()
    {
        using var handler = new ScriptedHttpHandler((_, body) =>
        {
            using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var spki = signingKey.ExportSubjectPublicKeyInfo();
            return Enrolled(body, Convert.ToBase64String(spki), new string('0', AgentCommandSigning.KeyIdLength));
        });
        var store = new RecordingCertificateStore();
        var sut = CreateSut(handler, store);

        var result = await sut.EnrollAsync("enrollment-token-for-test");

        Assert.True(result.IsFailure);
        Assert.Contains("[Enrollment.InvalidSigningKey]", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCertificateCount);
    }

    [Fact]
    public async Task Enrollment_ValidSigningKey_SucceedsWithKeyOnResult()
    {
        string? sentKey = null;
        string? sentKeyId = null;
        using var handler = new ScriptedHttpHandler((_, body) =>
        {
            using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var spki = signingKey.ExportSubjectPublicKeyInfo();
            sentKey = Convert.ToBase64String(spki);
            sentKeyId = AgentCommandSigning.ComputeKeyId(spki);
            return Enrolled(body, sentKey, sentKeyId);
        });
        var store = new RecordingCertificateStore();
        var sut = CreateSut(handler, store);

        var result = await sut.EnrollAsync("enrollment-token-for-test");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, store.StoreCertificateCount);
        Assert.Equal(sentKey, result.Value!.CommandSigningPublicKey);
        Assert.Equal(sentKeyId, result.Value.CommandSigningKeyId);
    }

    private EnrollmentService CreateSut(ScriptedHttpHandler handler, RecordingCertificateStore store) =>
        new(
            store,
            new ScriptedHttpClientFactory(handler),
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new FixedHardwareInventoryCollector(),
            clientCertificateProvider: new RecordingClientCertificateProvider(),
            identityProvider: new AgentIdentityProvider());

    /// <summary>Issues a certificate for the request's public key and carries the given signing key fields.</summary>
    private HttpResponseMessage Enrolled(string requestBody, string signingKeyBase64, string signingKeyId)
    {
        using var document = JsonDocument.Parse(requestBody);
        var publicKey = Convert.FromBase64String(document.RootElement.GetProperty("publicKey").GetString()!);
        using var leaf = _ca.Issue(publicKey, AgentCertificateProfile.SpiffeId(NodeId), null, null);

        var response = new EnrollNodeResponse(
            NodeId: NodeId,
            OrganizationId: OrganizationId,
            CertificateThumbprint: leaf.Thumbprint,
            Certificate: TestAgentCa.ToPem(leaf),
            CaCertificate: _ca.CertificatePem,
            NotBefore: leaf.NotBefore,
            NotAfter: leaf.NotAfter,
            CommandSigningPublicKey: signingKeyBase64,
            CommandSigningKeyId: signingKeyId);

        return AgentCertificateTestJson.Json(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(response, WebJsonOptions));
    }
}
