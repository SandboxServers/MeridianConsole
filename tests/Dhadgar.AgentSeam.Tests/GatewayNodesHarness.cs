extern alias gateway;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Nodes.Tests;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Forwarder;

using AgentEnrollmentService = Dhadgar.Agent.Core.Authentication.EnrollmentService;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>A request the Gateway forwarded to a cluster, recorded before it reached the backend.</summary>
/// <param name="ClusterId">The YARP cluster the request was forwarded to.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path after the Gateway's transforms (what the backend saw).</param>
public sealed record ForwardedRequest(string ClusterId, string Method, string Path);

/// <summary>
/// Gateway factory whose YARP forwarder sends every <c>nodes</c> request into the Nodes <see cref="TestServer"/>.
/// The connection to Nodes has no TLS certificate; the Gateway is the only trusted proxy and it is at 10.99.0.10.
/// </summary>
public sealed class GatewayTestFactory(TestServer nodesServer) : WebApplicationFactory<gateway::Program>
{
    /// <summary>The address Nodes sees as the Gateway's connection. It is inside the trusted-proxy range.</summary>
    public static readonly IPAddress GatewayAddress = IPAddress.Parse("10.99.0.10");

    private readonly ConcurrentQueue<ForwardedRequest> _forwarded = new();

    /// <summary>Every request the Gateway forwarded, in order. A rejected request that never reached a backend is absent.</summary>
    public IReadOnlyCollection<ForwardedRequest> ForwardedRequests => _forwarded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://panel.meridianconsole.com",
                ["ReverseProxy:Clusters:nodes:Destinations:d1:Address"] = "http://nodes.seam/",
                ["ReverseProxy:Clusters:nodes:HealthCheck:Active:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(new NodesForwarderHttpClientFactory(nodesServer, _forwarded));
        });
    }

    /// <summary>
    /// Returns a new invoker per call (YARP disposes the one it is given). Each one routes to Nodes with the
    /// Gateway's address as the remote address and no client certificate, and records what it forwards.
    /// </summary>
    private sealed class NodesForwarderHttpClientFactory(TestServer nodesServer, ConcurrentQueue<ForwardedRequest> forwarded) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
        {
            // Ownership passes to the HttpMessageInvoker (disposeHandler: true), which YARP disposes.
#pragma warning disable CA2000
            var recorder = new RecordingHandler(context.ClusterId, forwarded)
            {
                InnerHandler = nodesServer.CreateHandler(ctx => ctx.Connection.RemoteIpAddress = GatewayAddress),
            };
            return new HttpMessageInvoker(recorder, disposeHandler: true);
#pragma warning restore CA2000
        }
    }

    /// <summary>Enqueues each forwarded request, then passes it to the TestServer handler.</summary>
    private sealed class RecordingHandler(string clusterId, ConcurrentQueue<ForwardedRequest> forwarded) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            forwarded.Enqueue(new ForwardedRequest(clusterId, request.Method.Method, request.RequestUri!.AbsolutePath));
            return base.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>
/// Owns a Nodes host (real CA, mTLS on, Gateway as trusted proxy) and a Gateway host that forwards to it.
/// Disposes both factories.
/// </summary>
public sealed class GatewayNodesHarness : IDisposable
{
    private readonly WebApplicationFactory<Program> _nodes;
    private readonly GatewayTestFactory _gateway;

    /// <summary>Creates both hosts, writing the CA material to <paramref name="caDirectory"/>.</summary>
    public GatewayNodesHarness(string caDirectory)
    {
        NodesBase = new NodesWebApplicationFactory();
        _nodes = AgentSeamHarness.CreateFactoryWithRealCa(
            NodesBase,
            caDirectory,
            enableMtls: true,
            o =>
            {
                o.ForwardedCertificate.Enabled = true;
                o.ForwardedCertificate.TrustedProxies = ["10.99.0.0/16"];
            });
        _gateway = new GatewayTestFactory(_nodes.Server);
    }

    /// <summary>The Nodes factory the harness was built from. Its <c>TimeProvider</c> is the fake clock.</summary>
    public NodesWebApplicationFactory NodesBase { get; }

    /// <summary>The Nodes host, reached only through its own factory and the Gateway forwarder.</summary>
    public WebApplicationFactory<Program> Nodes => _nodes;

    /// <summary>The Gateway test server. Agents connect here.</summary>
    public TestServer GatewayServer => _gateway.Server;

    /// <summary>Requests the Gateway forwarded to a backend. Rejections that never reached a backend leave no entry.</summary>
    public IReadOnlyCollection<ForwardedRequest> ForwardedRequests => _gateway.ForwardedRequests;

    /// <summary>Creates a one-time enrollment token in the Nodes database.</summary>
    public Task<string> CreateEnrollmentTokenAsync(Guid orgId) =>
        AgentSeamHarness.CreateEnrollmentTokenAsync(_nodes, orgId);

    /// <summary>
    /// Enrolls a new agent through <c>/api/v1/agents/enroll</c> on the Gateway. The Gateway strips the prefix.
    /// </summary>
    public async Task<(EnrollmentResult Result, InMemoryCertificateStore Store)> EnrollThroughGatewayAsync(Guid orgId)
    {
        var token = await CreateEnrollmentTokenAsync(orgId);
        var store = new InMemoryCertificateStore();
        using var certificateProvider = new ClientCertificateProvider(store);
        var agent = AgentSeamHarness.CreateEnrollmentService(
            new TestServerClientFactory(GatewayServer, stripApiPrefix: false),
            store,
            NodesBase.TimeProvider,
            certificateProvider);

        var result = await agent.EnrollAsync(token);
        Xunit.Assert.True(result.IsSuccess, result.Error);
        return (result.Value!, store);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gateway.Dispose();
        _nodes.Dispose();
        NodesBase.Dispose();
    }
}
