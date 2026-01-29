using Dhadgar.Contracts;
using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Audit;

/// <summary>
/// Service for recording and querying audit logs.
/// All node operations are logged for security, compliance, and debugging.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Log an audit entry asynchronously.
    /// </summary>
    Task LogAsync(AuditEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Convenience method to log an audit entry with common parameters.
    /// </summary>
    Task LogAsync(
        string action,
        string resourceType,
        Guid? resourceId,
        AuditOutcome outcome,
        object? details = null,
        string? resourceName = null,
        Guid? organizationId = null,
        string? failureReason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Query audit logs with filtering and pagination.
    /// </summary>
    Task<PagedResponse<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

/// <summary>
/// Entry point for creating an audit log record.
/// </summary>
public sealed record AuditEntry
{
    /// <summary>Action performed (e.g., "node.created", "certificate.issued").</summary>
    public required string Action { get; init; }

    /// <summary>Type of resource affected.</summary>
    public required string ResourceType { get; init; }

    /// <summary>ID of the affected resource.</summary>
    public Guid? ResourceId { get; init; }

    /// <summary>Human-readable name of the resource.</summary>
    public string? ResourceName { get; init; }

    /// <summary>Organization ID (tenant).</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Outcome of the action.</summary>
    public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;

    /// <summary>Reason for failure or denial.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Additional context (will be serialized to JSON).</summary>
    public object? Details { get; init; }

    /// <summary>Override actor ID (normally captured from HttpContext).</summary>
    public string? ActorIdOverride { get; init; }

    /// <summary>Override actor type (normally inferred from context).</summary>
    public ActorType? ActorTypeOverride { get; init; }
}

/// <summary>
/// Query parameters for audit log searches.
/// </summary>
public sealed record AuditQuery
{
    /// <summary>Filter by organization ID (required for non-admin users).</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Filter by start date (inclusive). Uses DateTimeOffset for timezone clarity.
    /// The offset portion indicates the timezone; comparisons are done in UTC.
    /// </summary>
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>
    /// Filter by end date (inclusive). Uses DateTimeOffset for timezone clarity.
    /// If time is midnight (00:00:00), the entire day is included.
    /// The offset portion indicates the timezone; comparisons are done in UTC.
    /// </summary>
    public DateTimeOffset? EndDate { get; init; }

    /// <summary>Filter by actor ID.</summary>
    public string? ActorId { get; init; }

    /// <summary>Filter by action (supports wildcards like "node.*").</summary>
    public string? Action { get; init; }

    /// <summary>Filter by resource type.</summary>
    public string? ResourceType { get; init; }

    /// <summary>Filter by resource ID.</summary>
    public Guid? ResourceId { get; init; }

    /// <summary>Filter by outcome.</summary>
    public AuditOutcome? Outcome { get; init; }

    /// <summary>Filter by correlation ID.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Pagination page (1-based).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page (1-100). Values outside this range are clamped.</summary>
    public int Limit { get; init; } = 50;

    /// <summary>
    /// Gets the effective limit after clamping to valid range (1-100).
    /// Use this property instead of Limit directly for query execution.
    /// </summary>
    public int EffectiveLimit => Math.Clamp(Limit, 1, 100);
}

/// <summary>
/// DTO for audit log query results (SIEM-compatible format).
/// </summary>
public sealed record AuditLogDto
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>UTC timestamp.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Actor identifier.</summary>
    public string ActorId { get; init; } = string.Empty;

    /// <summary>Actor type.</summary>
    public string ActorType { get; init; } = string.Empty;

    /// <summary>Action performed.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Resource type.</summary>
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>Resource ID.</summary>
    public Guid? ResourceId { get; init; }

    /// <summary>Resource name.</summary>
    public string? ResourceName { get; init; }

    /// <summary>Organization ID.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Outcome.</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>Failure reason.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Additional details as JSON string.</summary>
    public string? Details { get; init; }

    /// <summary>Correlation ID.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Request ID.</summary>
    public string? RequestId { get; init; }

    /// <summary>Client IP address.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Client user agent.</summary>
    public string? UserAgent { get; init; }
}

/// <summary>
/// Well-known audit action constants.
/// </summary>
public static class AuditActions
{
    // Node operations
    public const string NodeCreated = "node.created";
    public const string NodeUpdated = "node.updated";
    public const string NodeDecommissioned = "node.decommissioned";
    public const string NodeMaintenanceStarted = "node.maintenance.started";
    public const string NodeMaintenanceEnded = "node.maintenance.ended";
    public const string NodeStatusChanged = "node.status.changed";

    // Enrollment operations
    public const string EnrollmentTokenCreated = "enrollment.token.created";
    public const string EnrollmentTokenRevoked = "enrollment.token.revoked";
    public const string EnrollmentCompleted = "enrollment.completed";
    public const string EnrollmentFailed = "enrollment.failed";

    // Certificate operations
    public const string CertificateIssued = "certificate.issued";
    public const string CertificateRevoked = "certificate.revoked";
    public const string CertificateRenewed = "certificate.renewed";

    // Capacity operations
    public const string CapacityReserved = "capacity.reserved";
    public const string CapacityClaimed = "capacity.claimed";
    public const string CapacityReleased = "capacity.released";
    public const string CapacityExpired = "capacity.expired";

    // Access control
    public const string AccessDenied = "access.denied";

    // Heartbeat operations
    public const string HeartbeatReceived = "heartbeat.received";
}

/// <summary>
/// Well-known resource type constants.
/// </summary>
public static class ResourceTypes
{
    public const string Node = "Node";
    public const string EnrollmentToken = "EnrollmentToken";
    public const string Certificate = "Certificate";
    public const string Capacity = "Capacity";
}
