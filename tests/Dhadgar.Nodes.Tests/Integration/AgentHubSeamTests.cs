using System.Net;
using System.Text.Json;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Identity;
using Dhadgar.Agent.Core.Telemetry;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Hubs;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using static Dhadgar.Nodes.Tests.Integration.AgentSeam.AgentSeamHarness;

namespace Dhadgar.Nodes.Tests.Integration;

/// <summary>
/// Seam test (S1): the real agent <see cref="ControlPlaneClient"/> talks to the real Nodes hub over a TestServer,
/// with mTLS enabled and a certificate issued by the real CA.
/// </summary>
[Collection("Nodes Integration")]
public sealed class AgentHubSeamTests : IDisposable
{
    private static readonly Uri NegotiateUri = new("https://localhost/hubs/agent/negotiate?negotiateVersion=1");

    private readonly NodesWebApplicationFactory _baseFactory;
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"agent-hub-seam-ca-{Guid.NewGuid()}");

    public AgentHubSeamTests(NodesWebApplicationFactory factory)
    {
        _baseFactory = factory;
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
    public async Task RealAgent_ConnectsWithEnrolledCertificate_AndHeartbeatUpdatesNodeHealth()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var orgId = Guid.NewGuid();
        var (enrolled, store) = await EnrollAgentAsync(factory, orgId);

        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var expectedHeartbeat = _baseFactory.TimeProvider.GetUtcNow().UtcDateTime;

        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateAgentClient(factory, enrolled, store, meter, activity);

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

        Node? node = null;
        NodeHealth? health = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            node = await db.Nodes.AsNoTracking().SingleOrDefaultAsync(n => n.Id == enrolled.NodeId);
            health = await db.NodeHealths.AsNoTracking().SingleOrDefaultAsync(h => h.NodeId == enrolled.NodeId);
            if (node?.LastHeartbeat == expectedHeartbeat && health?.CpuUsagePercent == 42.5)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        Assert.NotNull(node);
        Assert.NotNull(health);
        Assert.Equal(expectedHeartbeat, node.LastHeartbeat);
        Assert.Equal("2.0.0-seam", node.AgentVersion);
        Assert.Equal(42.5, health.CpuUsagePercent);
        Assert.Equal(10.0, health.MemoryUsagePercent);
        Assert.Equal(10.0, health.DiskUsagePercent);
        Assert.Equal(Dhadgar.Nodes.Data.Entities.NodeStatus.Online, node.Status);
    }

    [Fact]
    public void MappedHeartbeat_WithControlCharactersAndSurrogates_PassesNodesValidator()
    {
        var payload = new HeartbeatPayload
        {
            NodeId = Guid.NewGuid(),
            AgentVersion = "1.0\n2",
            Metrics = new SystemMetrics { CpuUsagePercent = 1 }
        };
        payload.Warnings.Add("disk error\r\nat C:\\x\0");
        payload.Warnings.Add(new string('x', AgentHeartbeatLimits.MaxHealthIssueLength - 1) + "\uD83D\uDE00");
        payload.Warnings.Add(string.Empty);

        var heartbeat = HeartbeatPayloadMapper.ToContract(payload);

        Assert.True(AgentHeartbeatValidator.IsValid(heartbeat));
        Assert.Equal("1.0 2", heartbeat.AgentVersion);
        Assert.Equal(2, heartbeat.HealthIssues!.Count);
    }

    [Fact]
    public async Task Negotiate_WithoutCertificate_Returns401ProblemJson()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        using var handler = new HttpClientHandler();
        using var http = new HttpClient(CertificateInjectingHandlerFactory(factory.Server)(handler));

        using var response = await PostNegotiateAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("certificate_required", problem.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Negotiate_WithForeignCaCertificate_Returns401()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, _) = await EnrollAgentAsync(factory, Guid.NewGuid());

        using var foreignCert = ForeignCa.CreateClientCertificate(enrolled.NodeId);
        using var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(foreignCert);
        using var http = new HttpClient(CertificateInjectingHandlerFactory(factory.Server)(handler));

        using var response = await PostNegotiateAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_issuer", problem.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Heartbeat_UpdatesCertificateNode_NotPayloadNode()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var orgId = Guid.NewGuid();
        var (enrolled, store) = await EnrollAgentAsync(factory, orgId);
        var otherNode = await SeedNodeAsync(factory, orgId, _baseFactory.TimeProvider.GetUtcNow());
        var otherNodeHeartbeat = otherNode.LastHeartbeat;

        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var expectedHeartbeat = _baseFactory.TimeProvider.GetUtcNow().UtcDateTime;

        using var meter = new AgentMeter();
        using var activity = new AgentActivitySource();
        await using var client = CreateAgentClient(factory, enrolled, store, meter, activity);

        await ConnectAgentAsync(client);
        // The payload names node B; the certificate names node A. The hub must trust the certificate.
        await SendAgentHeartbeatAsync(client, new HeartbeatPayload
        {
            NodeId = otherNode.Id,
            AgentVersion = "2.0.0-seam"
        });

        Node? enrolledNode = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            enrolledNode = await db.Nodes.AsNoTracking().SingleOrDefaultAsync(n => n.Id == enrolled.NodeId);
            if (enrolledNode?.LastHeartbeat == expectedHeartbeat)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        Assert.NotNull(enrolledNode);
        Assert.Equal(expectedHeartbeat, enrolledNode.LastHeartbeat);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var reloadedOther = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == otherNode.Id);
            Assert.Equal(otherNodeHeartbeat, reloadedOther.LastHeartbeat);
        }
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
    /// Heartbeat step. <see cref="IControlPlaneClient.SendHeartbeatAsync"/> takes the typed contract heartbeat
    /// and returns a <c>Result&lt;AgentHeartbeatAck&gt;</c>; the payload is mapped here.
    /// </summary>
    private static async Task SendAgentHeartbeatAsync(ControlPlaneClient client, HeartbeatPayload payload)
    {
        var result = await client.SendHeartbeatAsync(HeartbeatPayloadMapper.ToContract(payload));
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value.Acknowledged, result.Value.ErrorCode);
    }

    private static ControlPlaneClient CreateAgentClient(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        EnrollmentResult enrolled,
        InMemoryCertificateStore store,
        AgentMeter meter,
        AgentActivitySource activity)
    {
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
                HttpMessageHandlerFactory = CertificateInjectingHandlerFactory(factory.Server),
                Transports = HttpTransportType.LongPolling
            });
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
    /// Seeds a node into the derived factory's database (the same store the hub reads).
    /// </summary>
    private static async Task<Node> SeedNodeAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        Guid organizationId,
        DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        await db.Database.EnsureCreatedAsync();

        var node = new Node
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = "seam-node-b",
            DisplayName = "seam-node-b",
            Status = Dhadgar.Nodes.Data.Entities.NodeStatus.Online,
            Platform = "linux",
            AgentVersion = "1.0.0",
            LastHeartbeat = now.UtcDateTime,
            CreatedAt = now.UtcDateTime
        };

        db.Nodes.Add(node);
        await db.SaveChangesAsync();
        return node;
    }
}
