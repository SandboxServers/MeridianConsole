using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Console.Data.Entities;

/// <summary>
/// Audit log for commands executed via the console.
/// </summary>
public sealed class CommandAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Server the command was executed on.</summary>
    public Guid ServerId { get; set; }

    /// <summary>Organization that owns the server.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>User who executed the command.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Username for display purposes.</summary>
    [MaxLength(200)]
    public string? Username { get; set; }

    /// <summary>The command that was executed.</summary>
    [Required]
    [MaxLength(2000)]
    public string Command { get; set; } = string.Empty;

    /// <summary>Whether the command was allowed to execute.</summary>
    public bool WasAllowed { get; set; }

    /// <summary>Reason if command was blocked.</summary>
    [MaxLength(500)]
    public string? BlockReason { get; set; }

    /// <summary>Result status of the command.</summary>
    public CommandResultStatus ResultStatus { get; set; }

    /// <summary>When the command was executed.</summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Client IP address hash (for audit, not tracking).</summary>
    [MaxLength(64)]
    public string? ClientIpHash { get; set; }

    /// <summary>SignalR connection ID.</summary>
    [MaxLength(100)]
    public string? ConnectionId { get; set; }
}

/// <summary>
/// Result status of a console command.
/// </summary>
public enum CommandResultStatus
{
    /// <summary>Command executed successfully.</summary>
    Success = 0,

    /// <summary>Command was blocked by policy.</summary>
    Blocked = 1,

    /// <summary>Command failed to execute.</summary>
    Failed = 2,

    /// <summary>Command timed out.</summary>
    Timeout = 3,

    /// <summary>Server was not available.</summary>
    ServerUnavailable = 4
}
