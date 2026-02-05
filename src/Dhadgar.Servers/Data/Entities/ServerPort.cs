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

    /// <summary>Port number inside the container/process (1-65535).</summary>
    [Range(MinPort, MaxPort)]
    public int InternalPort { get; set; }

    /// <summary>Port number exposed externally (may differ from internal, 1-65535).</summary>
    [Range(MinPort, MaxPort)]
    public int ExternalPort { get; set; }

    /// <summary>Minimum valid port number.</summary>
    public const int MinPort = 1;

    /// <summary>Maximum valid port number.</summary>
    public const int MaxPort = 65535;

    /// <summary>Validates that port numbers are within valid range.</summary>
    public bool HasValidPorts() =>
        InternalPort is >= MinPort and <= MaxPort &&
        ExternalPort is >= MinPort and <= MaxPort;

    /// <summary>Whether this port is the primary connection port.</summary>
    public bool IsPrimary { get; set; }
}
