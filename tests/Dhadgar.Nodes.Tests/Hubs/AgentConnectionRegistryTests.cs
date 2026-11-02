using Dhadgar.Nodes.Hubs;

namespace Dhadgar.Nodes.Tests.Hubs;

public sealed class AgentConnectionRegistryTests
{
    [Fact]
    public void TwoConnections_ForOneNode_CountAsOneConnectedNode()
    {
        var registry = new AgentConnectionRegistry();
        var nodeId = Guid.NewGuid();

        registry.Add(nodeId, "a");
        registry.Add(nodeId, "b");

        Assert.True(registry.IsConnected(nodeId));
        Assert.Equal(1, registry.ConnectedNodeCount);
    }

    [Fact]
    public void RemovingOneConnection_LeavesNodeConnected_RemovingBoth_DisconnectsIt()
    {
        var registry = new AgentConnectionRegistry();
        var nodeId = Guid.NewGuid();
        registry.Add(nodeId, "a");
        registry.Add(nodeId, "b");

        registry.Remove(nodeId, "a");
        Assert.True(registry.IsConnected(nodeId));

        registry.Remove(nodeId, "b");
        Assert.False(registry.IsConnected(nodeId));
        Assert.Equal(0, registry.ConnectedNodeCount);
    }

    [Fact]
    public void RemovingUnknownConnection_IsNoOp()
    {
        var registry = new AgentConnectionRegistry();
        var nodeId = Guid.NewGuid();
        registry.Add(nodeId, "a");

        registry.Remove(nodeId, "missing");
        registry.Remove(Guid.NewGuid(), "a");

        Assert.True(registry.IsConnected(nodeId));
        Assert.Equal(1, registry.ConnectedNodeCount);
    }

    [Fact]
    public void ConnectedNodeCount_CountsDistinctNodes()
    {
        var registry = new AgentConnectionRegistry();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        registry.Add(first, "a");
        registry.Add(second, "b");
        registry.Add(second, "c");
        Assert.Equal(2, registry.ConnectedNodeCount);

        registry.Remove(first, "a");
        Assert.Equal(1, registry.ConnectedNodeCount);
        Assert.False(registry.IsConnected(first));
    }
}
