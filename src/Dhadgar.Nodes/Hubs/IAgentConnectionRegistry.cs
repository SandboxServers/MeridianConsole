namespace Dhadgar.Nodes.Hubs;

/// <summary>
/// Tracks which nodes have at least one open agent hub connection. Used by the hub on connect and disconnect,
/// and by features that need to know whether a node can be reached (VS-15 uses <see cref="IsConnected"/>).
/// </summary>
public interface IAgentConnectionRegistry
{
    /// <summary>Records a connection for a node. A node may hold several connections at once.</summary>
    void Add(Guid nodeId, string connectionId);

    /// <summary>Removes a connection for a node. A no-op when the connection is not recorded.</summary>
    void Remove(Guid nodeId, string connectionId);

    /// <summary>True when the node has at least one recorded connection.</summary>
    bool IsConnected(Guid nodeId);

    /// <summary>The number of distinct nodes with at least one recorded connection.</summary>
    int ConnectedNodeCount { get; }
}
