using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Servers.Data.Entities;

/// <summary>
/// Port mapping for a server.
/// </summary>
public sealed class ServerPort : BaseEntity
{
    /// <summary>Reference to the parent server.</summary>
    public Guid ServerId { get; set; }
    public Server Server { get; set; } = null!;

    /// <summary>Port name/identifier (e.g., "game", "query", "rcon").</summary>
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Protocol type.</summary>
    [Required]
    [MaxLength(10)]
    public string Protocol { get; set; } = "tcp";

    /// <summary>Port number inside the container/process.</summary>
    public int InternalPort { get; set; }

    /// <summary>Port number exposed externally (may differ from internal).</summary>
    public int ExternalPort { get; set; }

    /// <summary>Whether this port is the primary connection port.</summary>
    public bool IsPrimary { get; set; }
}
