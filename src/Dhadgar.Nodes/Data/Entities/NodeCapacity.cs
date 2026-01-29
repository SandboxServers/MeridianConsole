namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Capacity tracking for resource allocation decisions.
/// </summary>
public sealed class NodeCapacity
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }

    /// <summary>Maximum number of game servers this node can host.</summary>
    public int MaxGameServers { get; set; }

    /// <summary>Current number of game servers running.</summary>
    public int CurrentGameServers { get; set; }

    /// <summary>Available memory for new allocations in bytes.</summary>
    public long AvailableMemoryBytes { get; set; }

    /// <summary>Available disk space for new allocations in bytes.</summary>
    public long AvailableDiskBytes { get; set; }

    /// <summary>When this capacity data was last updated.</summary>
    public DateTime UpdatedAt { get; set; }

    // Navigation property
    public Node Node { get; set; } = null!;
}
