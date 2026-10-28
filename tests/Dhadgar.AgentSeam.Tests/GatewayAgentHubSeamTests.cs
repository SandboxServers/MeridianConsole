using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Core.Telemetry;
using Dhadgar.Contracts.Agents;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Hubs;
using Dhadgar.Nodes.Services;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using static Dhadgar.Nodes.Tests.Integration.AgentSeam.AgentSeamHarness;

namespace Dhadgar.AgentSeam.Tests;

/// <summary>
/// Seam tests (S2): the real agent talks to the Gateway, which forwards to the real Nodes host. The Gateway is the
/// only path to Nodes. Nodes trusts the certificate the Gateway forwards and never sees the agent's TLS connection.
/// </summary>
public sealed class GatewayAgentHubSeamTests : IDisposable
{
    private static readonly Uri NegotiateUri = new("https://localhost/hubs/agent/negotiate?negotiateVersion=1");

    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"gateway-agent-seam-ca-{Guid.NewGuid()}");

    public GatewayAgentHubSeamTests()
    {
        Directory.CreateDirectory(_caDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_caDirectory, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task RealAgent_EnrollsAndHeartbeatsThroughGateway()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var orgId = Guid.NewGuid();
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(orgId);

        harness.NodesBase.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var expectedHeartbeat = harness.NodesBase.TimeProvider.GetUtcNow().UtcDateTime;

        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateAgentClient(harness.GatewayServer, enrolled, store, meter, activity);

        await ConnectAgentAsync(client);
        await SendAgentHeartbeatAsync(client, new HeartbeatPayload
        {
            NodeId = enrolled.NodeId,
            AgentVersion = "2.0.0-seam",
            Metrics = new SystemMetrics
            {
                CpuUsagePercent = 42.5,
                TotalMemoryBytes = 1000,
                AvailableMemoryBytes = 900,
                Disks = [new DiskMetrics { Name = "root", TotalBytes = 100, AvailableBytes = 90 }]
            }
        });

        var node = await WaitForHeartbeatAsync(harness, enrolled.NodeId, expectedHeartbeat);
        Assert.NotNull(node);
        Assert.Equal(expectedHeartbeat, node.LastHeartbeat);
        Assert.Equal("2.0.0-seam", node.AgentVersion);
    }

    [Fact]
    public async Task Hub_WithoutClientCertificate_IsRejectedAtGateway()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        using var handler = new HttpClientHandler();
        using var http = new HttpClient(CertificateInjectingHandlerFactory(harness.GatewayServer)(handler));

        using var response = await PostNegotiateAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertRejectedByGatewayChallenge(response);
    }

    [Fact]
    public async Task Hub_SpoofedForwardedHeaderWithoutTlsCert_IsRejected()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var (_, enrolledStore) = await harness.EnrollThroughGatewayAsync(Guid.NewGuid());
        var spoofed = Convert.ToBase64String(enrolledStore.StoredClientCertificate!.RawData);

        // No connection certificate. The header carries a valid enrolled certificate.
        using var http = new HttpClient(new HeaderInjectingHandler(
            AgentCertificateProfile.ForwardedCertificateHeader,
            spoofed)
        {
            InnerHandler = CertificateInjectingHandlerFactory(harness.GatewayServer)(new HttpClientHandler())
        });

        using var response = await PostNegotiateAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertRejectedByGatewayChallenge(response);
    }

    [Fact]
    public async Task Hub_SpoofedHeaderAlongsideRealCert_UsesTlsCert()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var orgId = Guid.NewGuid();
        var (agentA, storeA) = await harness.EnrollThroughGatewayAsync(orgId);
        var (agentB, storeB) = await harness.EnrollThroughGatewayAsync(orgId);
        var bCertificate = Convert.ToBase64String(storeB.StoredClientCertificate!.RawData);

        var nodeBBefore = await ReadLastHeartbeatAsync(harness, agentB.NodeId);
        harness.NodesBase.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var expectedHeartbeat = harness.NodesBase.TimeProvider.GetUtcNow().UtcDateTime;

        // Agent A's own TLS certificate goes on the connection. It also sends B's certificate in the header.
        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateAgentClient(
            harness.GatewayServer,
            agentA,
            storeA,
            meter,
            activity,
            certificateHandler => new HeaderInjectingHandler(AgentCertificateProfile.ForwardedCertificateHeader, bCertificate)
            {
                InnerHandler = certificateHandler
            });

        await ConnectAgentAsync(client);
        await SendAgentHeartbeatAsync(client, new HeartbeatPayload
        {
            NodeId = agentA.NodeId,
            AgentVersion = "2.0.0-seam"
        });

        var nodeA = await WaitForHeartbeatAsync(harness, agentA.NodeId, expectedHeartbeat);
        Assert.NotNull(nodeA);
        Assert.Equal(expectedHeartbeat, nodeA.LastHeartbeat);

        var nodeBAfter = await ReadLastHeartbeatAsync(harness, agentB.NodeId);
        Assert.Equal(nodeBBefore, nodeBAfter);
    }

    [Fact]
    public async Task Telemetry_ThroughGateway_IsAccepted()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        // The agent client does not expose its hub connection, so the test invokes Telemetry on a hub connection
        // that uses the same certificate, protocol header and Gateway handler as ControlPlaneClient.
        using var recorder = new ResponseRecordingHandler();
        await using var connection = CreateHubConnectionThroughGateway(harness.GatewayServer, cert, recorder);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHubAck>(
            nameof(IAgentHub.Telemetry),
            new AgentTelemetry(harness.NodesBase.TimeProvider.GetUtcNow(), new Dictionary<string, double> { ["cpu"] = 1 }));

        Assert.True(ack.Accepted, ack.ErrorCode);
        Assert.True(await WaitForConnectedAsync(harness, enrolled.NodeId), "Nodes' registry does not report the node as connected.");
    }

    [Fact]
    public async Task Connect_ForDecommissionedNode_IsClosedByNodes()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        using (var scope = harness.Nodes.Services.CreateScope())
        {
            var nodes = scope.ServiceProvider.GetRequiredService<INodeService>();
            var decommissioned = await nodes.DecommissionNodeAsync(enrolled.OrganizationId, enrolled.NodeId);
            Assert.True(decommissioned.Success, decommissioned.Error);
        }

        await ConnectExpectingNodesRefusalAsync(harness, enrolled.NodeId, cert);
    }

    [Fact]
    public async Task Connect_ForDecommissionedNodeWithActiveCertificate_IsClosedByNodes()
    {
        using var harness = new GatewayNodesHarness(_caDirectory);
        var (enrolled, store) = await harness.EnrollThroughGatewayAsync(Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        // INodeService also revokes the node's certificates, and the certificate check refuses first. Setting only the
        // status leaves the certificate usable, so a refusal here can only come from the connect-time node check.
        using (var scope = harness.Nodes.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == enrolled.NodeId);
            node.Status = Dhadgar.Nodes.Data.Entities.NodeStatus.Decommissioned;
            await db.SaveChangesAsync();
        }

        await ConnectExpectingNodesRefusalAsync(harness, enrolled.NodeId, cert);
    }

    /// <summary>
    /// Connects through the Gateway with the client certificate and asserts that Nodes refused the connection.
    /// </summary>
    private static async Task ConnectExpectingNodesRefusalAsync(GatewayNodesHarness harness, Guid nodeId, X509Certificate2 cert)
    {
        using var recorder = new ResponseRecordingHandler();
        await using var connection = CreateHubConnectionThroughGateway(harness.GatewayServer, cert, recorder);
        var closed = WatchForClose(connection);

        var startFailure = await Record.ExceptionAsync(() => connection.StartAsync());
        if (startFailure is null)
        {
            // The connection opened and Nodes aborts it in OnConnectedAsync, so it must close.
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            var refused = Assert.IsType<HttpRequestException>(startFailure);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

            // Proof that Nodes answered: the Gateway hosts no hub, and its own JwtBearer challenge is the only 401
            // that carries a Bearer WWW-Authenticate. A refusal from Nodes has none.
            Assert.Contains(recorder.Responses, r => r.Status == HttpStatusCode.Unauthorized && !r.GatewayChallenge);
        }

        Assert.False(harness.Nodes.Services.GetRequiredService<IAgentConnectionRegistry>().IsConnected(nodeId));
    }

    /// <summary>
    /// Builds a raw hub connection to the Gateway with the agent's client certificate and protocol header.
    /// </summary>
    private static HubConnection CreateHubConnectionThroughGateway(
        TestServer gatewayServer,
        X509Certificate2 cert,
        ResponseRecordingHandler recorder) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost" + AgentHubProtocol.Path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.ClientCertificates = [cert];
                options.HttpMessageHandlerFactory = inner =>
                {
                    recorder.InnerHandler = CertificateInjectingHandlerFactory(gatewayServer)(inner);
                    return recorder;
                };
                options.Headers[AgentHubProtocol.VersionHeader] = AgentHubProtocol.CurrentVersion;
            })
            .Build();

    /// <summary>
    /// Records the failed responses that come back on the hub connection, and whether each one is the Gateway's challenge.
    /// </summary>
    private sealed class ResponseRecordingHandler : DelegatingHandler
    {
        public ConcurrentQueue<RecordedResponse> Responses { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var gatewayChallenge = response.Headers.WwwAuthenticate.Any(h => h.Scheme == "Bearer");
                Responses.Enqueue(new RecordedResponse(response.StatusCode, gatewayChallenge));
            }

            return response;
        }
    }

    private sealed record RecordedResponse(HttpStatusCode Status, bool GatewayChallenge);

    private static TaskCompletionSource WatchForClose(HubConnection connection)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        return closed;
    }

    /// <summary>
    /// Polls Nodes' connection registry until the node is connected, or the deadline passes.
    /// </summary>
    private static async Task<bool> WaitForConnectedAsync(GatewayNodesHarness harness, Guid nodeId)
    {
        var registry = harness.Nodes.Services.GetRequiredService<IAgentConnectionRegistry>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (registry.IsConnected(nodeId))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return registry.IsConnected(nodeId);
    }

    /// <summary>
    /// Connect step. <see cref="IControlPlaneClient.ConnectAsync"/> returns a <c>Result</c>; a refused connection fails here.
    /// </summary>
    private static async Task ConnectAgentAsync(ControlPlaneClient client)
    {
        var result = await client.ConnectAsync();
        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// Heartbeat step. The payload is mapped to the typed contract, and the ack must be accepted.
    /// </summary>
    private static async Task SendAgentHeartbeatAsync(ControlPlaneClient client, HeartbeatPayload payload)
    {
        var result = await client.SendHeartbeatAsync(HeartbeatPayloadMapper.ToContract(payload));
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value.Acknowledged, result.Value.ErrorCode);
    }

    /// <summary>
    /// Builds a real <see cref="ControlPlaneClient"/> whose hub calls go to the Gateway test server.
    /// </summary>
    /// <param name="outerLayer">Optional extra handler layer placed outside the certificate-injecting handler.</param>
    private static ControlPlaneClient CreateAgentClient(
        TestServer gatewayServer,
        EnrollmentResult enrolled,
        InMemoryCertificateStore store,
        AgentMeter meter,
        AgentActivitySource activity,
        Func<HttpMessageHandler, HttpMessageHandler>? outerLayer = null)
    {
        var certificateHandler = CertificateInjectingHandlerFactory(gatewayServer);
        var identity = new AgentIdentityProvider();
        identity.Set(new AgentIdentity(enrolled.NodeId, enrolled.OrganizationId, DateTimeOffset.UtcNow));

        return new ControlPlaneClient(
            Options.Create(new AgentOptions
            {
                ControlPlane = { Endpoint = "https://localhost" }
            }),
            store,
            meter,
            activity,
            NullLogger<ControlPlaneClient>.Instance,
            identity,
            new ControlPlaneHubTransport
            {
                HttpMessageHandlerFactory = outerLayer is null
                    ? certificateHandler
                    : inner => outerLayer(certificateHandler(inner)),
                Transports = HttpTransportType.LongPolling
            });
    }

    /// <summary>
    /// The Gateway's JwtBearer challenge answers with a Bearer <c>WWW-Authenticate</c> and no problem+json body.
    /// Nodes answers with problem+json, so this tells the two rejections apart.
    /// </summary>
    private static void AssertRejectedByGatewayChallenge(HttpResponseMessage response)
    {
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<HttpResponseMessage> PostNegotiateAsync(HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, NegotiateUri)
        {
            Content = new ByteArrayContent([])
        };
        return await http.SendAsync(request);
    }

    /// <summary>
    /// Polls the Nodes database until the node's heartbeat equals <paramref name="expected"/>, or the deadline passes.
    /// </summary>
    private static async Task<Node?> WaitForHeartbeatAsync(GatewayNodesHarness harness, Guid nodeId, DateTime expected)
    {
        Node? node = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            using var scope = harness.Nodes.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            node = await db.Nodes.AsNoTracking().SingleOrDefaultAsync(n => n.Id == nodeId);
            if (node?.LastHeartbeat == expected)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return node;
    }

    private static async Task<DateTime?> ReadLastHeartbeatAsync(GatewayNodesHarness harness, Guid nodeId)
    {
        using var scope = harness.Nodes.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == nodeId);
        return node.LastHeartbeat;
    }

    /// <summary>
    /// Adds one header to every outgoing request. It models a caller that sends a header it should not.
    /// </summary>
    private sealed class HeaderInjectingHandler(string headerName, string headerValue) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove(headerName);
            request.Headers.TryAddWithoutValidation(headerName, headerValue);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
