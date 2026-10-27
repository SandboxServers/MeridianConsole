extern alias gateway;

using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Forwarder;

namespace Dhadgar.Seams.Tests.Infrastructure;

/// <summary>
/// Runs the real Gateway in-process. YARP's outbound client is replaced so each cluster is served by an
/// in-process handler (usually a backend <c>TestServer</c>) instead of a socket.
/// </summary>
public sealed class GatewaySeamFactory : WebApplicationFactory<gateway::Program>
{
    private readonly IReadOnlyDictionary<string, Func<HttpMessageHandler>> _backends;

    public GatewaySeamFactory(IReadOnlyDictionary<string, Func<HttpMessageHandler>> backends)
    {
        _backends = backends;
        Forwarder = new SeamForwarderFactory(backends);
    }

    /// <summary>Records which clusters the Gateway forwarded requests to.</summary>
    public SeamForwarderFactory Forwarder { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new Dictionary<string, string?>();
            foreach (var clusterId in _backends.Keys)
            {
                settings[$"ReverseProxy:Clusters:{clusterId}:Destinations:d1:Address"] = $"http://{clusterId}.seams/";
            }

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            SeamJwt.Apply(services);

            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(Forwarder);

            // Inserted at index 0 so the certificate shim is the first middleware in the pipeline.
            services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter, SeamClientCertificateStartupFilter>());
        });
    }
}

/// <summary>
/// Routes YARP's outbound requests to in-process handlers keyed by cluster id. Unknown clusters get a 503.
/// </summary>
public sealed class SeamForwarderFactory(IReadOnlyDictionary<string, Func<HttpMessageHandler>> backends) : IForwarderHttpClientFactory
{
    private readonly ConcurrentQueue<string> _forwardedClusters = new();

    /// <summary>Cluster ids of every request the Gateway forwarded, in arrival order.</summary>
    public IReadOnlyList<string> ForwardedClusters => _forwardedClusters.ToArray();

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        var clusterId = context.ClusterId;
        if (clusterId is null || !backends.TryGetValue(clusterId, out var backend))
        {
#pragma warning disable CA2000 // Ownership passes to the invoker.
            return new HttpMessageInvoker(new StatusHandler(HttpStatusCode.ServiceUnavailable), disposeHandler: false);
#pragma warning restore CA2000
        }

#pragma warning disable CA2000 // Ownership passes to the invoker; the seam host lives for the whole test.
        var recording = new RecordingHandler(clusterId, _forwardedClusters) { InnerHandler = backend() };
#pragma warning restore CA2000
        return new HttpMessageInvoker(recording, disposeHandler: false);
    }

    private sealed class RecordingHandler(string clusterId, ConcurrentQueue<string> log) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            log.Enqueue(clusterId);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}

/// <summary>
/// Stands in for the TLS handshake, which <c>TestServer</c> cannot perform. A test client sends its certificate
/// (base64 DER) in <c>X-Seam-Client-Cert</c>; this filter moves it onto <c>Connection.ClientCertificate</c> and
/// removes the header, so the Gateway sees the same certificate it would after a real mTLS handshake.
/// </summary>
public sealed class SeamClientCertificateStartupFilter : IStartupFilter
{
    /// <summary>Header that carries the base64 DER client certificate.</summary>
    public const string HeaderName = "X-Seam-Client-Cert";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var encoded))
                {
                    context.Request.Headers.Remove(HeaderName);
                    try
                    {
                        var der = Convert.FromBase64String(encoded.ToString());
                        context.Connection.ClientCertificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(der);
                    }
                    catch (FormatException)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }
                }

                await nextMiddleware(context);
            });

            next(app);
        };
    }
}
