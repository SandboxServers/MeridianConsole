using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Primary node entity representing a customer-owned machine running the Meridian agent.
/// </summary>
public sealed class Node
{
    public Guid Id { get; set; }

    /// <summary>Organization that owns this node.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Unique name within organization (used for identification).</summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-friendly display name.</summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>Current lifecycle status.</summary>
    public NodeStatus Status { get; set; } = NodeStatus.Enrolling;

    /// <summary>Version of the agent software running on this node.</summary>
    [MaxLength(50)]
    public string? AgentVersion { get; set; }

    /// <summary>Operating system platform: "linux" or "windows".</summary>
    [Required]
    [MaxLength(20)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>Last time a heartbeat was received from this node.</summary>
    public DateTime? LastHeartbeat { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Soft delete timestamp. When set, node is considered decommissioned.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// User-defined tags for filtering and categorization.
    /// Stored as JSONB array in PostgreSQL.
    /// </summary>
    /// <remarks>
    /// CA1002 suppressed: EF Core requires List&lt;T&gt; for JSONB column mapping with PostgreSQL.
    /// Using Collection&lt;T&gt; or IList&lt;T&gt; breaks the JSON serialization.
    /// </remarks>
#pragma warning disable CA1002 // Do not expose generic lists - required for EF Core JSONB mapping
    public List<string> Tags { get; set; } = [];
#pragma warning restore CA1002

    /// <summary>
    /// PostgreSQL xmin-based optimistic concurrency token.
    /// </summary>
    public uint RowVersion { get; set; }

    // Navigation properties
    public NodeHardwareInventory? HardwareInventory { get; set; }
    public NodeHealth? Health { get; set; }
    public NodeCapacity? Capacity { get; set; }
    public ICollection<AgentCertificate> Certificates { get; set; } = [];
}
