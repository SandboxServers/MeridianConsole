using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Hardware specification snapshot collected from a node.
/// </summary>
public sealed class NodeHardwareInventory
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }

    /// <summary>Operating system hostname.</summary>
    [Required]
    [MaxLength(255)]
    public string Hostname { get; set; } = string.Empty;

    /// <summary>OS version string (e.g., "Ubuntu 22.04 LTS", "Windows Server 2022").</summary>
    [MaxLength(200)]
    public string? OsVersion { get; set; }

    /// <summary>Number of CPU cores available.</summary>
    public int CpuCores { get; set; }

    /// <summary>Total physical memory in bytes.</summary>
    public long MemoryBytes { get; set; }

    /// <summary>Total disk space in bytes.</summary>
    public long DiskBytes { get; set; }

    /// <summary>JSON array of network interface information.</summary>
    public string? NetworkInterfaces { get; set; }

    /// <summary>When this inventory data was collected from the agent.</summary>
    public DateTime CollectedAt { get; set; }

    // Navigation property
    public Node Node { get; set; } = null!;
}
