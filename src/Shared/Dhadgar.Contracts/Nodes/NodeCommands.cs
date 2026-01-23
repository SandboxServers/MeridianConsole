namespace Dhadgar.Contracts.Nodes;

/// <summary>
/// Command to request a health check for a specific node.
/// </summary>
public record CheckNodeHealth(
    Guid NodeId);

/// <summary>
/// Command to update a node's capacity configuration.
/// </summary>
public record UpdateNodeCapacity(
    Guid NodeId,
    int MaxGameServers);
