using System.Net.Http;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Core.Telemetry;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dhadgar.Seams.Tests.Infrastructure;

/// <summary>
/// A real agent (real <see cref="EnrollmentService"/> and <see cref="ControlPlaneClient"/>) whose HTTP and
/// SignalR traffic goes to the Gateway's in-process handler instead of a socket.
/// </summary>
public sealed class SeamAgent : IDisposable
{
    private readonly Func<HttpMessageHandler> _gatewayHandler;
    private readonly AgentMeter _meter = new();
    private readonly AgentActivitySource _activitySource = new();

    public SeamAgent(Func<HttpMessageHandler> gatewayHandler)
    {
        _gatewayHandler = gatewayHandler;
        Store = new InMemoryCertificateStore();
        ClientCertificates = new ClientCertificateProvider(Store);
        HttpClients = new SeamHttpClientFactory(_gatewayHandler, Store);
    }

    internal InMemoryCertificateStore Store { get; }

    private SeamHttpClientFactory HttpClients { get; }

    private ClientCertificateProvider ClientCertificates { get; }

    private AgentIdentityProvider Identity { get; } = new();

    /// <summary>Enrolls through the Gateway with the real enrollment service.</summary>
    public Task<Result<EnrollmentResult>> EnrollAsync(string enrollmentToken, CancellationToken cancellationToken = default)
    {
        var enrollment = new EnrollmentService(
            Store,
            HttpClients,
            Options.Create(new AgentOptions()),
            NullLogger<EnrollmentService>.Instance,
            new HardwareInventoryCollector(NullLogger<HardwareInventoryCollector>.Instance),
            ClientCertificates,
            Identity);

        return enrollment.EnrollAsync(enrollmentToken, cancellationToken);
    }

    /// <summary>
    /// Builds the real hub client for an enrolled node, or for an unenrolled agent when <paramref name="nodeId"/>
    /// is null. The hub's transport is routed through the Gateway by <see cref="SeamHubConfigurator"/>.
    /// </summary>
    public ControlPlaneClient CreateControlPlaneClient(Guid? nodeId, Guid orgId)
    {
        // Enrollment sets the identity; an explicit node ID covers callers that skipped enrollment.
        if (nodeId.HasValue)
        {
            Identity.Set(new AgentIdentity(nodeId.Value, orgId, DateTimeOffset.UtcNow));
        }

        var options = new AgentOptions
        {
            ControlPlane = { Endpoint = "https://localhost" }
        };

        return new ControlPlaneClient(
            Options.Create(options),
            Store,
            _meter,
            _activitySource,
            NullLogger<ControlPlaneClient>.Instance,
            Identity,
            configurators: [new SeamHubConfigurator(Store, _gatewayHandler)]);
    }

    public void Dispose()
    {
        ClientCertificates.Dispose();
        _meter.Dispose();
        _activitySource.Dispose();
    }

    /// <summary>Like IHttpClientFactory: a new client per call, which the agent disposes after each request.</summary>
    private sealed class SeamHttpClientFactory(Func<HttpMessageHandler> gatewayHandler, InMemoryCertificateStore store) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            // The mTLS client name carries the client certificate; the plain client does not.
            var attachCertificate = name == "ControlPlaneMtls";
#pragma warning disable CA2000 // Ownership passes to the HttpClient, which the agent disposes.
            var handler = new ClientCertHeaderHandler(store, attachCertificate) { InnerHandler = gatewayHandler() };
#pragma warning restore CA2000
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("https://localhost/")
            };
        }
    }
}

/// <summary>
/// Stands in for the client certificate on the TLS handshake: copies the store's certificate into
/// <c>X-Seam-Client-Cert</c> (see <see cref="SeamClientCertificateStartupFilter"/>).
/// </summary>
public sealed class ClientCertHeaderHandler(ICertificateStore store, bool attachClientCertificate = true) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (attachClientCertificate)
        {
            using var certificate = store.GetClientCertificate();
            if (certificate is not null)
            {
                request.Headers.TryAddWithoutValidation(
                    SeamClientCertificateStartupFilter.HeaderName,
                    Convert.ToBase64String(certificate.RawData));
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Routes the hub connection through the Gateway in-process. Long polling is used because the in-memory
/// handler cannot upgrade to a WebSocket.
/// </summary>
public sealed class SeamHubConfigurator(ICertificateStore store, Func<HttpMessageHandler> gatewayHandler) : IHubConnectionConfigurator
{
    public void Configure(HttpConnectionOptions options)
    {
        options.Transports = HttpTransportType.LongPolling;
#pragma warning disable CA2000 // Ownership passes to the hub connection.
        options.HttpMessageHandlerFactory = _ => new ClientCertHeaderHandler(store) { InnerHandler = gatewayHandler() };
#pragma warning restore CA2000
    }
}
