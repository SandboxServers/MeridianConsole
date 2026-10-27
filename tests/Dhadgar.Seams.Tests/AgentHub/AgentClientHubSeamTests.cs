extern alias nodes;

using System.Net;
using System.Net.Http;
using Dhadgar.Agent.Core.Health;
using nodes::Dhadgar.Nodes.Data;
using nodes::Dhadgar.Nodes.Data.Entities;
using Dhadgar.Seams.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Seams.Tests.AgentHub;

/// <summary>
/// Seam: the agent hub client talks to Nodes through the Gateway.
/// </summary>
public sealed class AgentClientHubSeamTests
{
    [Fact(Skip = "Pending VS-13: no /hubs/agent hub and no typed agent wire yet")]
    public async Task Heartbeat_ThroughGateway_UpdatesNode()
    {
        using var host = new SeamHost();
        var orgId = Guid.NewGuid();
        var token = await host.CreateEnrollmentTokenAsync(orgId);
        using var agent = host.CreateAgent();
        var enrolled = await agent.EnrollAsync(token);
        Assert.True(enrolled.IsSuccess, enrolled.Error);
        var nodeId = enrolled.Value!.NodeId;

        DateTime? heartbeatBefore;
        using (var scope = host.Nodes.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            heartbeatBefore = (await db.Nodes.SingleAsync(n => n.Id == nodeId)).LastHeartbeat;
        }

        await using (var client = agent.CreateControlPlaneClient(nodeId, orgId))
        {
            await client.ConnectAsync();
            await client.SendHeartbeatAsync(HeartbeatPayloadMapper.ToContract(new HeartbeatPayload
            {
                NodeId = nodeId,
                AgentVersion = "seams",
                Metrics = new SystemMetrics
                {
                    CpuUsagePercent = 12.5,
                    TotalMemoryBytes = 8L * 1024 * 1024 * 1024,
                    AvailableMemoryBytes = 4L * 1024 * 1024 * 1024
                }
            }));
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            using var scope = host.Nodes.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
            var node = await db.Nodes.SingleAsync(n => n.Id == nodeId);
            var latestHealth = await db.NodeHealths
                .Where(h => h.NodeId == nodeId)
                .OrderByDescending(h => h.ReportedAt)
                .FirstOrDefaultAsync();

            if (node.LastHeartbeat != heartbeatBefore && latestHealth?.CpuUsagePercent == 12.5)
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Node did not record the heartbeat within 5 s (LastHeartbeat={node.LastHeartbeat}, CPU={latestHealth?.CpuUsagePercent}).");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    [Fact(Skip = "Pending VS-13: no /hubs/agent hub and no typed agent wire yet")]
    public async Task Connect_WithoutCertificate_IsRejected()
    {
        using var host = new SeamHost();
        using var agent = host.CreateAgent();
        // NodeId null: ControlPlaneClient's own "certificate required" guard only covers enrolled agents, so the
        // negotiate request reaches the Gateway with no client certificate and the control plane must refuse it.
        await using var client = agent.CreateControlPlaneClient(nodeId: null, Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.ConnectAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        // The request must have reached the backend's mTLS check, not failed in the agent or at the Gateway.
        Assert.Contains("nodes", host.Forwarder.ForwardedClusters);
    }
}
