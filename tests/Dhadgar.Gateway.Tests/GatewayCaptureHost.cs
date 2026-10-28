using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Dhadgar.Gateway.Tests;

/// <summary>
/// Shared host, request and certificate helpers for Gateway tests that assert what the YARP forwarder received.
/// </summary>
internal static class GatewayCaptureHost
{
    /// <summary>
    /// Builds a Gateway app whose forwarder records requests into <paramref name="capture"/>.
    /// </summary>
    internal static WebApplicationFactory<Program> CreateApp(
        WebApplicationFactory<Program> factory,
        CaptureHandler capture,
        IEnumerable<KeyValuePair<string, string?>>? extraSettings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            foreach (var setting in extraSettings ?? Array.Empty<KeyValuePair<string, string?>>())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new List<KeyValuePair<string, string?>>
                {
                    new KeyValuePair<string, string?>("ReverseProxy:Clusters:nodes:Destinations:d1:Address", "http://nodes.test/"),
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
                configureServices?.Invoke(services);
            });
        });
    }

    /// <summary>
    /// Sends one request with an optional client certificate and an optional bearer token.
    /// </summary>
    internal static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> app,
        HttpMethod method,
        string path,
        X509Certificate2? certificate)
    {
        using var request = new HttpRequestMessage(method, path);
        return await SendAsync(app, request, certificate);
    }

    /// <summary>
    /// Sends <paramref name="request"/> with an optional client certificate, remote IP and bearer token.
    /// </summary>
    internal static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> app,
        HttpRequestMessage request,
        X509Certificate2? certificate,
        IPAddress? remoteIp = null,
        string? bearerToken = null)
    {
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

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
    internal static X509Certificate2 CreateCertificate(string issuerCommonName)
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

    internal sealed record CapturedRequest(Uri? Uri, IReadOnlyDictionary<string, string[]> Headers)
    {
        public bool HasHeader(string name) => Headers.ContainsKey(name);

        public string? Header(string name) =>
            Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
    }

    /// <summary>
    /// Records every request YARP proxies and answers 200 without touching the network.
    /// Active health probes (/healthz) share this forwarder client, so they are answered but not recorded.
    /// </summary>
    internal sealed class CaptureHandler : HttpMessageHandler
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

    internal sealed class CaptureForwarderHttpClientFactory(CaptureHandler handler) : IForwarderHttpClientFactory, IDisposable
    {
        private readonly HttpMessageInvoker _invoker = new(handler);

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => _invoker;

        public void Dispose() => _invoker.Dispose();
    }
}
