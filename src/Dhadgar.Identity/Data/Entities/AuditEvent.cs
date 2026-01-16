using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

/// <summary>
/// Persistent audit trail for security-sensitive operations.
/// Used for compliance, debugging, and security analysis.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Type of event (e.g., "user.created", "role.assigned", "membership.invited")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = null!;

    /// <summary>
    /// User affected by the event (null for system events)
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Organization context (null for user-level events)
    /// </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// User who performed the action (null for system/automated events)
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// Type of resource affected (e.g., "user", "organization", "role", "membership")
    /// </summary>
    [MaxLength(50)]
    public string? TargetType { get; set; }

    /// <summary>
    /// ID of the specific resource affected
    /// </summary>
    public Guid? TargetId { get; set; }

    /// <summary>
    /// Client IP address (for web requests)
    /// </summary>
    [MaxLength(45)]
    public string? ClientIp { get; set; }

    /// <summary>
    /// User agent string (for web requests)
    /// </summary>
    [MaxLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Additional event-specific details stored as JSON
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Correlation ID for distributed tracing
    /// </summary>
    [MaxLength(50)]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
