namespace Dhadgar.Nodes.Data.Entities;

/// <summary>
/// Audit log entry for all node-related operations.
/// SIEM-compatible format with comprehensive context capture.
/// </summary>
public sealed class NodeAuditLog
{
    /// <summary>Unique identifier for the audit log entry.</summary>
    public Guid Id { get; set; }

    /// <summary>UTC timestamp when the action occurred.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>User ID, service name, node ID, or "system" for automated actions.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>Type of actor performing the action.</summary>
    public ActorType ActorType { get; set; }

    /// <summary>Action performed (e.g., "node.created", "certificate.issued").</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Type of resource affected (e.g., "Node", "Certificate", "EnrollmentToken").</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>ID of the affected resource, if applicable.</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>Human-readable name of the resource, for display purposes.</summary>
    public string? ResourceName { get; set; }

    /// <summary>Organization ID (tenant) for multi-tenancy filtering.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>Outcome of the action.</summary>
    public AuditOutcome Outcome { get; set; }

    /// <summary>Reason for failure or denial, if applicable.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Additional context as JSON (JSONB in PostgreSQL).</summary>
    public string? Details { get; set; }

    /// <summary>Correlation ID for distributed tracing.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Request ID for request tracking.</summary>
    public string? RequestId { get; set; }

    /// <summary>Client IP address.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Client user agent string.</summary>
    public string? UserAgent { get; set; }
}

/// <summary>
/// Type of actor performing an audited action.
/// </summary>
public enum ActorType
{
    /// <summary>Human user via API.</summary>
    User = 0,

    /// <summary>Backend service (inter-service communication).</summary>
    Service = 1,

    /// <summary>Node agent.</summary>
    Agent = 2,

    /// <summary>System/automated action (e.g., background jobs).</summary>
    System = 3
}

/// <summary>
/// Outcome of an audited action.
/// </summary>
public enum AuditOutcome
{
    /// <summary>Action completed successfully.</summary>
    Success = 0,

    /// <summary>Action failed due to an error.</summary>
    Failure = 1,

    /// <summary>Action was denied due to authorization.</summary>
    Denied = 2
}
