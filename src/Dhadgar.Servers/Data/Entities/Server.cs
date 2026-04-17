// CA1724: Type name 'Server' conflicts with namespace 'Microsoft.SqlServer.Server'.
// This is an intentional domain entity name - we don't reference SqlServer and the
// conflict only exists in the .NET Framework (not .NET Core).
#pragma warning disable CA1724

using System.ComponentModel.DataAnnotations;
using Dhadgar.Shared.Data;

namespace Dhadgar.Servers.Data.Entities;

/// <summary>
/// Primary entity representing a game server instance.
/// </summary>
public sealed class Server : BaseEntity, ITenantScoped
{
    /// <summary>Organization that owns this server.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Node this server is deployed on (null if not yet placed).</summary>
    public Guid? NodeId { get; set; }

    /// <summary>Unique name within organization.</summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-friendly display name.</summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>Game type identifier (e.g., "minecraft", "valheim", "ark").</summary>
    [Required]
    [MaxLength(50)]
    public string GameType { get; set; } = string.Empty;

    /// <summary>Current lifecycle status.</summary>
    public ServerStatus Status { get; set; } = ServerStatus.Created;

    /// <summary>Current power state of the server process.</summary>
    public ServerPowerState PowerState { get; set; } = ServerPowerState.Off;

    /// <summary>CPU limit in millicores (1000 = 1 CPU core).</summary>
    [Range(1, int.MaxValue, ErrorMessage = "CPU limit must be at least 1 millicore")]
    public int CpuLimitMillicores { get; set; }

    /// <summary>Memory limit in megabytes.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Memory limit must be at least 1 MB")]
    public int MemoryLimitMb { get; set; }

    /// <summary>Disk space limit in megabytes.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Disk limit must be at least 1 MB")]
    public int DiskLimitMb { get; set; }

    /// <summary>Capacity reservation token from the Nodes service.</summary>
    public Guid? ReservationToken { get; set; }

    /// <summary>Last time the server was started.</summary>
    public DateTime? LastStartedAt { get; set; }

    /// <summary>Last time the server was stopped.</summary>
    public DateTime? LastStoppedAt { get; set; }

    /// <summary>Total uptime in seconds (cumulative).</summary>
    public long TotalUptimeSeconds { get; set; }

    /// <summary>Number of times the server has crashed.</summary>
    public int CrashCount { get; set; }

    /// <summary>
    /// User-defined tags for filtering and categorization.
    /// Stored as JSONB array in PostgreSQL.
    /// </summary>
#pragma warning disable CA1002 // Do not expose generic lists - required for EF Core JSONB mapping
    public List<string> Tags { get; set; } = [];
#pragma warning restore CA1002

    // Navigation properties
    public ServerConfiguration? Configuration { get; set; }
    public Guid? TemplateId { get; set; }
    public ServerTemplate? Template { get; set; }
    public ICollection<ServerPort> Ports { get; set; } = [];
}
