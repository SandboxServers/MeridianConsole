using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Dhadgar.Contracts.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Dhadgar.Gateway.Tests;

[Collection("Gateway Integration")]
public class AgentCertificateForwardingTests
{
    private const string NodesDestination = "http://nodes.test/";
    private const string ForeignIssuerCommonName = "Some Other Certificate Authority";

    private readonly GatewayWebApplicationFactory _factory;

    public AgentCertificateForwardingTests(GatewayWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HubRoute_WithAgentCertificate_ForwardsBase64Der()
    {
        using var agentCertificate = CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);

        var response = await SendAsync(app, HttpMethod.Post, "/hubs/agent/negotiate", agentCertificate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proxied = Assert.Single(capture.Requests);
        Assert.Equal(Convert.ToBase64String(agentCertificate.RawData), proxied.Header(AgentCertificateProfile.ForwardedCertificateHeader));
    }

    [Fact]
    public async Task AgentsRoute_WithoutCertificate_Returns401_AndDoesNotProxy()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);

        var response = await SendAsync(app, HttpMethod.Post, "/api/v1/agents/3f2a/heartbeat", certificate: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(capture.Requests);
    }

    [Fact]
    public async Task AgentsRoute_WithForeignIssuer_IsRejected()
    {
        using var foreignCertificate = CreateCertificate(ForeignIssuerCommonName);
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);

        var response = await SendAsync(app, HttpMethod.Post, "/api/v1/agents/3f2a/heartbeat", foreignCertificate);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401 or 403 but got {(int)response.StatusCode}.");
        Assert.Empty(capture.Requests);
    }

    [Fact]
    public async Task EnrollRoute_SpoofedHeader_IsStripped()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agents/enroll");
        request.Headers.TryAddWithoutValidation(AgentCertificateProfile.ForwardedCertificateHeader, "abc");

        var response = await SendAsync(app, request, certificate: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proxied = Assert.Single(capture.Requests);
        Assert.False(proxied.HasHeader(AgentCertificateProfile.ForwardedCertificateHeader));
    }

    [Fact]
    public async Task IdentityRoute_SpoofedHeader_IsStripped()
    {
        // Stand-in for the tenant (JWT) routes: a spoofed header must not survive on an anonymous route either.
        // The tenant routes need a bearer token, which the test host does not issue.
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/.well-known/openid-configuration");
        request.Headers.TryAddWithoutValidation(AgentCertificateProfile.ForwardedCertificateHeader, "abc");

        var response = await SendAsync(app, request, certificate: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proxied = Assert.Single(capture.Requests);
        Assert.False(proxied.HasHeader(AgentCertificateProfile.ForwardedCertificateHeader));
    }

    [Fact]
    public async Task PerAgent_ForgedIssuerCertificatePerRequest_IsRateLimitedByIp()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture, new[]
        {
            new KeyValuePair<string, string?>("RateLimiting:Policies:PerAgent:PermitLimit", "3"),
            new KeyValuePair<string, string?>("RateLimiting:Policies:PerAgent:WindowSeconds", "60"),
        });
        var remoteIp = IPAddress.Parse("203.0.113.10");
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            // A fresh self-signed "agent CA" and leaf per request is the forgery.
            using var response = await PostForgedNegotiateAsync(app, remoteIp);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests },
            statuses);
        Assert.Equal(3, capture.Requests.Count);
    }

    [Fact]
    public async Task AgentRoute_WithoutCertificate_FloodIsRateLimited()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture, new[]
        {
            new KeyValuePair<string, string?>("RateLimiting:Policies:PerAgent:PermitLimit", "2"),
            new KeyValuePair<string, string?>("RateLimiting:Policies:PerAgent:WindowSeconds", "60"),
        });
        var remoteIp = IPAddress.Parse("203.0.113.40");
        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? lastResponse = null;

        try
        {
            for (var i = 0; i < 3; i++)
            {
                var response = await PostNegotiateAsync(app, certificate: null, remoteIp);
                lastResponse?.Dispose();
                lastResponse = response;
                statuses.Add(response.StatusCode);
            }

            Assert.Equal(
                new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests },
                statuses);
            Assert.True(lastResponse!.Headers.Contains("Retry-After"), "Expected a Retry-After header on the 429.");
            Assert.Empty(capture.Requests);
        }
        finally
        {
            lastResponse?.Dispose();
        }
    }

    [Fact]
    public async Task AgentRoute_WrongIssuerCertificate_FloodIsRateLimited()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture, new[]
        {
            new KeyValuePair<string, string?>("RateLimiting:Policies:PerAgent:PermitLimit", "2"),
            new KeyValuePair<string, string?>("RateLimiting:Policies:PerAgent:WindowSeconds", "60"),
        });
        var remoteIp = IPAddress.Parse("203.0.113.41");
        var statuses = new List<HttpStatusCode>();

        using var foreignCertificate = CreateCertificate(ForeignIssuerCommonName);
        for (var i = 0; i < 3; i++)
        {
            using var response = await PostNegotiateAsync(app, foreignCertificate, remoteIp);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests },
            statuses);
        Assert.Empty(capture.Requests);
    }

    [Fact]
    public async Task NonAgentRoute_LimitedByRoutePolicy_NotByGlobal()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture, new[]
        {
            new KeyValuePair<string, string?>("RateLimiting:Policies:Auth:PermitLimit", "2"),
            new KeyValuePair<string, string?>("RateLimiting:Policies:Auth:WindowSeconds", "60"),
            new KeyValuePair<string, string?>("RateLimiting:Policies:Global:PermitLimit", "100"),
        });
        var remoteIp = IPAddress.Parse("203.0.113.20");
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            using var response = await GetOpenIdConfigurationAsync(app, remoteIp);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests },
            statuses);
        Assert.Equal(2, capture.Requests.Count);
    }

    [Fact]
    public async Task NonAgentRoute_GlobalExhausted_RoutePolicyStillAllows()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture, new[]
        {
            new KeyValuePair<string, string?>("RateLimiting:Policies:Global:PermitLimit", "1"),
            new KeyValuePair<string, string?>("RateLimiting:Policies:Global:WindowSeconds", "60"),
            new KeyValuePair<string, string?>("RateLimiting:Policies:Auth:PermitLimit", "100"),
        });
        var remoteIp = IPAddress.Parse("203.0.113.21");
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            using var response = await GetOpenIdConfigurationAsync(app, remoteIp);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK },
            statuses);
        Assert.Equal(3, capture.Requests.Count);
    }

    [Fact]
    public async Task AgentRoute_NotLimitedByGlobal()
    {
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture, new[]
        {
            new KeyValuePair<string, string?>("RateLimiting:Policies:Global:PermitLimit", "2"),
            new KeyValuePair<string, string?>("RateLimiting:Policies:PerAgent:PermitLimit", "100"),
        });
        var remoteIp = IPAddress.Parse("203.0.113.30");

        for (var i = 0; i < 3; i++)
        {
            using var response = await PostForgedNegotiateAsync(app, remoteIp);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(3, capture.Requests.Count);
    }

    [Fact]
    public async Task HubRoute_SpoofedHeaderWithRealCertificate_ForwardsRealCertificate()
    {
        using var cert = CreateCertificate(AgentCertificateProfile.CaCommonName);
        using var capture = new CaptureHandler();
        using var app = CreateApp(capture);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/agent/negotiate");
        request.Headers.TryAddWithoutValidation(AgentCertificateProfile.ForwardedCertificateHeader, "abc");

        using var response = await SendAsync(app, request, cert);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proxied = Assert.Single(capture.Requests);
        Assert.Single(proxied.Headers[AgentCertificateProfile.ForwardedCertificateHeader]);
        Assert.Equal(Convert.ToBase64String(cert.RawData), proxied.Header(AgentCertificateProfile.ForwardedCertificateHeader));
    }

    private WebApplicationFactory<Program> CreateApp(CaptureHandler capture, IEnumerable<KeyValuePair<string, string?>>? extraSettings = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            foreach (var setting in extraSettings ?? Array.Empty<KeyValuePair<string, string?>>())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new List<KeyValuePair<string, string?>>
                {
                    new KeyValuePair<string, string?>("ReverseProxy:Clusters:nodes:Destinations:d1:Address", NodesDestination),
                    new KeyValuePair<string, string?>("ReverseProxy:Clusters:nodes:HealthCheck:Active:Enabled", "false"),
                    new KeyValuePair<string, string?>("ReverseProxy:Clusters:identity:Destinations:d1:Address", "http://identity.test/"),
                    new KeyValuePair<string, string?>("ReverseProxy:Clusters:identity:HealthCheck:Active:Enabled", "false"),
                };
                if (extraSettings is not null)
                {
                    settings.AddRange(extraSettings);
                }

                config.AddInMemoryCollection(settings);
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IForwarderHttpClientFactory>(new CaptureForwarderHttpClientFactory(capture));
            });
        });
    }

    /// <summary>
    /// Sends one POST to the agent hub route. The request is created and disposed inside this method,
    /// so callers in loops do not own a disposable per iteration.
    /// </summary>
    private static async Task<HttpResponseMessage> PostNegotiateAsync(
        WebApplicationFactory<Program> app,
        X509Certificate2? certificate,
        IPAddress? remoteIp = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/agent/negotiate");
        return await SendAsync(app, request, certificate, remoteIp);
    }

    /// <summary>
    /// Sends one GET for the OpenID configuration document, unauthenticated. The request is disposed here.
    /// </summary>
    private static async Task<HttpResponseMessage> GetOpenIdConfigurationAsync(
        WebApplicationFactory<Program> app,
        IPAddress remoteIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/.well-known/openid-configuration");
        return await SendAsync(app, request, certificate: null, remoteIp);
    }

    /// <summary>
    /// Sends one POST to the agent hub route with a freshly issued forged certificate.
    /// </summary>
    private static async Task<HttpResponseMessage> PostForgedNegotiateAsync(
        WebApplicationFactory<Program> app,
        IPAddress remoteIp)
    {
        using var forged = CreateCertificate(AgentCertificateProfile.CaCommonName);
        return await PostNegotiateAsync(app, forged, remoteIp);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> app,
        HttpMethod method,
        string path,
        X509Certificate2? certificate)
    {
        using var request = new HttpRequestMessage(method, path);
        return await SendAsync(app, request, certificate);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> app,
        HttpRequestMessage request,
        X509Certificate2? certificate,
        IPAddress? remoteIp = null)
    {
        // The handler presents the certificate as the connection's client certificate (Kestrel would set it on TLS).
        var handler = app.Server.CreateHandler(ctx =>
        {
            ctx.Connection.ClientCertificate = certificate;
            if (remoteIp is not null)
            {
                ctx.Connection.RemoteIpAddress = remoteIp;
            }
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        return await client.SendAsync(request);
    }

    /// <summary>Builds a client certificate whose issuer CN is <paramref name="issuerCommonName"/>. Caller disposes.</summary>
    private static X509Certificate2 CreateCertificate(string issuerCommonName)
    {
        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caRequest = new CertificateRequest($"CN={issuerCommonName}", caKey, HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var caCertificate = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=agent-test", leafKey, HashAlgorithmName.SHA256);
        return leafRequest.Create(
            caCertificate,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            [0x01, 0x02, 0x03, 0x04]);
    }

    private sealed record CapturedRequest(Uri? Uri, IReadOnlyDictionary<string, string[]> Headers)
    {
        public bool HasHeader(string name) => Headers.ContainsKey(name);

        public string? Header(string name) =>
            Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
    }

    /// <summary>
    /// Records every request YARP proxies and answers 200 without touching the network.
    /// Active health probes (/healthz) share this forwarder client, so they are answered but not recorded.
    /// </summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly List<CapturedRequest> _requests = [];

        public List<CapturedRequest> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/healthz")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            var headers = request.Headers.ToDictionary(
                h => h.Key,
                h => h.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            _requests.Add(new CapturedRequest(request.RequestUri, headers));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class CaptureForwarderHttpClientFactory(CaptureHandler handler) : IForwarderHttpClientFactory, IDisposable
    {
        private readonly HttpMessageInvoker _invoker = new(handler);

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => _invoker;

        public void Dispose() => _invoker.Dispose();
    }
}
