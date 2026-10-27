using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Contracts.Nodes;

namespace Dhadgar.Agent.Core.Tests.Authentication;

/// <summary>
/// A throwaway ECDSA P-384 CA that signs agent certificates over whatever SubjectPublicKeyInfo
/// the test hands it, so tests can prove the agent's own key is what gets bound.
/// </summary>
internal sealed class TestAgentCa : IDisposable
{
    private readonly ECDsa _key;
    private readonly X509Certificate2 _certificate;

    public TestAgentCa()
    {
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest("CN=Test Agent CA", _key, HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    /// <summary>The CA certificate as a PEM block, the form Nodes returns.</summary>
    public string CertificatePem => new(PemEncoding.Write("CERTIFICATE", _certificate.RawData));

    /// <summary>The CA certificate as a fresh copy the caller owns.</summary>
    public X509Certificate2 CopyCertificate() => X509CertificateLoader.LoadCertificate(_certificate.RawData);

    /// <summary>
    /// Issues a leaf certificate for <paramref name="subjectPublicKeyInfo"/>. When
    /// <paramref name="spiffeUri"/> is null the certificate carries no URI SAN.
    /// </summary>
    public X509Certificate2 Issue(byte[] subjectPublicKeyInfo, string? spiffeUri)
    {
        var subjectKey = PublicKey.CreateFromSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
        var request = new CertificateRequest(
            new X500DistinguishedName("CN=test-agent"),
            subjectKey,
            HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        if (spiffeUri is not null)
        {
            var san = new SubjectAlternativeNameBuilder();
            san.AddUri(new Uri(spiffeUri));
            request.CertificateExtensions.Add(san.Build());
        }

        var generator = X509SignatureGenerator.CreateForECDsa(_key);
        using var issued = request.Create(
            _certificate.SubjectName,
            generator,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            RandomNumberGenerator.GetBytes(16));

        return X509CertificateLoader.LoadCertificate(issued.RawData);
    }

    /// <summary>Renders a certificate as a PEM CERTIFICATE block.</summary>
    public static string ToPem(X509Certificate2 certificate) =>
        new(PemEncoding.Write("CERTIFICATE", certificate.RawData));

    public void Dispose()
    {
        _certificate.Dispose();
        _key.Dispose();
    }
}

/// <summary>
/// In-memory <see cref="ICertificateStore"/> that copies everything it is given, because the
/// services under test dispose the certificate and zero the key buffer right after the call.
/// </summary>
internal sealed class RecordingCertificateStore : ICertificateStore
{
    private X509Certificate2? _clientCertificate;

    public RecordingCertificateStore(X509Certificate2? initial = null)
    {
        _clientCertificate = initial is null ? null : X509CertificateLoader.LoadCertificate(initial.RawData);
    }

    public int StoreCertificateCount { get; private set; }

    public int StoreCaCertificateCount { get; private set; }

    /// <summary>A copy of the PKCS#8 bytes handed to <see cref="StoreCertificateAsync"/>.</summary>
    public byte[]? StoredPrivateKey { get; private set; }

    /// <summary>The certificate most recently stored, as a DER copy.</summary>
    public byte[]? StoredCertificateDer { get; private set; }

    public X509Certificate2? GetClientCertificate() =>
        _clientCertificate is null ? null : X509CertificateLoader.LoadCertificate(_clientCertificate.RawData);

    public Task StoreCertificateAsync(
        X509Certificate2 certificate,
        byte[] privateKey,
        CancellationToken cancellationToken = default)
    {
        StoreCertificateCount++;
        StoredPrivateKey = privateKey.ToArray();
        StoredCertificateDer = certificate.RawData.ToArray();
        _clientCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return Task.CompletedTask;
    }

    public Task RemoveCertificateAsync(CancellationToken cancellationToken = default)
    {
        _clientCertificate = null;
        return Task.CompletedTask;
    }

    public X509Certificate2? GetCaCertificate() => null;

    public Task StoreCaCertificateAsync(
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        StoreCaCertificateCount++;
        return Task.CompletedTask;
    }

    public bool NeedsRenewal(int thresholdDays) => false;
}

/// <summary>
/// Counts <see cref="Invalidate"/> calls so tests can prove the handshake certificate was reset.
/// </summary>
internal sealed class RecordingClientCertificateProvider : IClientCertificateProvider
{
    public int InvalidateCount { get; private set; }

    public X509Certificate2? Current => null;

    public void Invalidate() => InvalidateCount++;
}

/// <summary>
/// Routes requests to a delegate. The delegate sees the request body (empty when there is none).
/// </summary>
internal sealed class ScriptedHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return respond(request, LastRequestBody ?? string.Empty);
    }
}

/// <summary>
/// Hands out one HttpClient per call over a shared handler, pointed at an HTTPS test host.
/// The handler is not disposed with the client, so the test can read it afterwards.
/// </summary>
internal sealed class ScriptedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
    {
        BaseAddress = new Uri("https://control.test/")
    };
}

/// <summary>
/// Fixed hardware inventory so enrollment tests do not probe the machine.
/// </summary>
internal sealed class FixedHardwareInventoryCollector : IHardwareInventoryCollector
{
    public HardwareInventoryRequest Collect() =>
        new("test-host", null, 4, 8L * 1024 * 1024 * 1024, 100L * 1024 * 1024 * 1024, null);
}

internal static class AgentCertificateTestJson
{
    /// <summary>Returns the base64 SubjectPublicKeyInfo carried in a JSON body's <c>publicKey</c>.</summary>
    public static byte[] ReadPublicKey(string body)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);
        return Convert.FromBase64String(document.RootElement.GetProperty("publicKey").GetString()!);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, mediaType)
        };
}
