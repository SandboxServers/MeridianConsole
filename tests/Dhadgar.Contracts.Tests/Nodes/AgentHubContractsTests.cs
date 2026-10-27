using System.Text.Json;
using Dhadgar.Contracts.Nodes;
using Xunit;

namespace Dhadgar.Contracts.Tests.Nodes;

/// <summary>
/// Pins the wire names of the agent hub DTOs. A renamed property is a silent wire break,
/// so these tests fail on any change to the serialized shape.
/// </summary>
public sealed class AgentHubContractsTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AgentHeartbeat_SerializesWithWebDefaults_ToPinnedWireNames()
    {
        var heartbeat = new AgentHeartbeat(
            CpuUsagePercent: 12.5,
            MemoryUsagePercent: 40,
            DiskUsagePercent: 70,
            ActiveGameServers: 2,
            AgentVersion: "1.2.3",
            HealthIssues: ["x"]);

        var json = JsonSerializer.Serialize(heartbeat, WebOptions);

        Assert.Equal(
            """{"cpuUsagePercent":12.5,"memoryUsagePercent":40,"diskUsagePercent":70,"activeGameServers":2,"agentVersion":"1.2.3","healthIssues":["x"]}""",
            json);

        var roundTripped = JsonSerializer.Deserialize<AgentHeartbeat>(json, WebOptions);
        Assert.NotNull(roundTripped);
        Assert.Equal(12.5, roundTripped.CpuUsagePercent);
        Assert.Equal(40.0, roundTripped.MemoryUsagePercent);
        Assert.Equal(70.0, roundTripped.DiskUsagePercent);
        Assert.Equal(2, roundTripped.ActiveGameServers);
        Assert.Equal("1.2.3", roundTripped.AgentVersion);
        Assert.NotNull(roundTripped.HealthIssues);
        Assert.Equal(new[] { "x" }, roundTripped.HealthIssues);
    }

    [Fact]
    public void AgentHeartbeatAck_SerializesWithWebDefaults_ToPinnedWireNames()
    {
        var serverTime = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var ack = new AgentHeartbeatAck(
            Acknowledged: false,
            ServerTime: serverTime,
            ErrorCode: AgentHubErrorCodes.NodeNotFound);

        var json = JsonSerializer.Serialize(ack, WebOptions);

        Assert.Equal(
            """{"acknowledged":false,"serverTime":"2026-10-27T12:00:00+00:00","errorCode":"node_not_found"}""",
            json);

        var roundTripped = JsonSerializer.Deserialize<AgentHeartbeatAck>(json, WebOptions);
        Assert.NotNull(roundTripped);
        Assert.False(roundTripped.Acknowledged);
        Assert.Equal(serverTime, roundTripped.ServerTime);
        Assert.Equal(AgentHubErrorCodes.NodeNotFound, roundTripped.ErrorCode);
    }

    [Fact]
    public void SpiffeId_FormatsNodeIdWithHyphens()
    {
        var nodeId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var spiffeId = AgentCertificateProfile.SpiffeId(nodeId);

        Assert.Equal("spiffe://meridianconsole.com/nodes/11111111-2222-3333-4444-555555555555", spiffeId);
    }

    [Fact]
    public void NodeGroup_IsStable()
    {
        var nodeId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var first = AgentHubProtocol.NodeGroup(nodeId);
        var second = AgentHubProtocol.NodeGroup(nodeId);

        Assert.Equal("node:11111111-2222-3333-4444-555555555555", first);
        Assert.Equal(first, second);
    }
}
