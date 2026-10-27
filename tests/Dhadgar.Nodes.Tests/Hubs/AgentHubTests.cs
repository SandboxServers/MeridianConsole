using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Nodes.Data;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Nodes.Hubs;
using Dhadgar.Nodes.Tests.Integration.AgentSeam;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Dhadgar.Nodes.Tests.Integration.AgentSeam.AgentSeamHarness;

namespace Dhadgar.Nodes.Tests.Hubs;

/// <summary>
/// Hub tests (P04): a raw SignalR client with an enrolled agent certificate talks to <see cref="AgentHub"/>
/// over a TestServer, with mTLS enabled and certificates issued by the real CA.
/// </summary>
[Collection("Nodes Integration")]
public sealed class AgentHubTests : IDisposable
{
    private readonly NodesWebApplicationFactory _baseFactory;
    private readonly string _caDirectory = Path.Combine(Path.GetTempPath(), $"agent-hub-ca-{Guid.NewGuid()}");

    public AgentHubTests(NodesWebApplicationFactory factory)
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
    public async Task Heartbeat_Valid_AcknowledgesAndUpdatesHealth()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var expectedHeartbeat = _baseFactory.TimeProvider.GetUtcNow().UtcDateTime;

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(42.5, 40, 70, 2, "2.0.0-hub", ["x"]));

        Assert.True(ack.Acknowledged);
        Assert.Null(ack.ErrorCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == enrolled.NodeId);
        var health = await db.NodeHealths.AsNoTracking().SingleAsync(h => h.NodeId == enrolled.NodeId);
        Assert.Equal(expectedHeartbeat, node.LastHeartbeat);
        Assert.Equal("2.0.0-hub", node.AgentVersion);
        Assert.Equal(42.5, health.CpuUsagePercent);
        Assert.Equal(2, health.ActiveGameServers);
    }

    [Fact]
    public async Task Heartbeat_CpuOver100_ReturnsInvalidHeartbeat_AndLeavesDbUnchanged()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        var (lastHeartbeatBefore, cpuBefore) = await ReadNodeAndCpuAsync(factory, enrolled.NodeId);

        _baseFactory.TimeProvider.Advance(TimeSpan.FromMinutes(2));

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(150, 40, 70, 2, "2.0.0-hub", null));

        Assert.False(ack.Acknowledged);
        Assert.Equal(AgentHubErrorCodes.InvalidHeartbeat, ack.ErrorCode);

        var (lastHeartbeatAfter, cpuAfter) = await ReadNodeAndCpuAsync(factory, enrolled.NodeId);
        Assert.Equal(lastHeartbeatBefore, lastHeartbeatAfter);
        Assert.Equal(cpuBefore, cpuAfter);
    }

    [Fact]
    public async Task Heartbeat_DecommissionedNode_ReturnsNodeDecommissioned()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (enrolled, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == enrolled.NodeId);
            node.Status = NodeStatus.Decommissioned;
            await db.SaveChangesAsync();
        }

        await using var connection = CreateHubConnection(factory, cert);
        await connection.StartAsync();

        var ack = await connection.InvokeAsync<AgentHeartbeatAck>(
            nameof(IAgentHub.Heartbeat),
            new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", null));

        Assert.False(ack.Acknowledged);
        Assert.Equal(AgentHubErrorCodes.NodeDecommissioned, ack.ErrorCode);
    }

    [Fact]
    public async Task Connect_WithUnsupportedProtocolHeader_IsClosedByServer()
    {
        using var factory = CreateFactoryWithRealCa(_baseFactory, _caDirectory, enableMtls: true);
        var (_, store) = await EnrollAgentAsync(factory, Guid.NewGuid());
        using var cert = store.GetClientCertificate()!;

        await using var connection = CreateHubConnection(factory, cert, protocolVersion: "2");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var rejected = false;
        try
        {
            await connection.StartAsync(cts.Token);
            // A connection the server kept open would acknowledge this heartbeat.
            await connection.InvokeAsync<AgentHeartbeatAck>(
                nameof(IAgentHub.Heartbeat),
                new AgentHeartbeat(10, 10, 10, 0, "2.0.0-hub", null),
                cts.Token);
        }
        catch (Exception)
        {
            rejected = true;
        }

        Assert.True(rejected);
    }

    [Fact]
    public void AgentHub_ImplementsContractInterface()
    {
        Assert.True(typeof(IAgentHub).IsAssignableFrom(typeof(AgentHub)));
    }

    private HubConnection CreateHubConnection(
        WebApplicationFactory<Program> factory,
        X509Certificate2 cert,
        string? protocolVersion = null)
    {
        return new HubConnectionBuilder()
            .WithUrl("https://localhost" + AgentHubProtocol.Path, o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.ClientCertificates = new X509CertificateCollection { cert };
                o.HttpMessageHandlerFactory = CertificateInjectingHandlerFactory(factory.Server);
                if (protocolVersion is not null)
                {
                    o.Headers[AgentHubProtocol.VersionHeader] = protocolVersion;
                }
            })
            .Build();
    }

    private static async Task<(DateTime? LastHeartbeat, double? CpuUsagePercent)> ReadNodeAndCpuAsync(
        WebApplicationFactory<Program> factory,
        Guid nodeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == nodeId);
        var health = await db.NodeHealths.AsNoTracking().SingleOrDefaultAsync(h => h.NodeId == nodeId);
        return (node.LastHeartbeat, health?.CpuUsagePercent);
    }
}
