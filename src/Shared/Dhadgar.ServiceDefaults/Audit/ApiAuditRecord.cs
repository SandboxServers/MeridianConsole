using System.ComponentModel.DataAnnotations;

namespace Dhadgar.ServiceDefaults.Audit;

/// <summary>
/// Entity for recording authenticated HTTP requests for compliance and analysis.
/// </summary>
/// <remarks>
/// <para>
/// This entity captures metadata about API requests, NOT domain-level events.
/// For domain events (like "user.created", "role.assigned"), see Identity's <c>AuditEvent</c> entity.
/// </para>
/// <para>
/// Each service that wants HTTP audit logging should:
/// <list type="number">
///   <item>Add <see cref="ApiAuditRecord"/> to their DbContext</item>
///   <item>Implement <see cref="IAuditDbContext"/> on their DbContext</item>
///   <item>Call <see cref="AuditExtensions.AddAuditInfrastructure{TContext}"/> in service configuration</item>
/// </list>
/// </para>
/// <para>
/// Following database-per-service pattern, each service maintains its own audit table.
/// Cross-service audit queries can use PostgreSQL foreign data wrappers or Grafana Loki.
/// </para>
/// </remarks>
public sealed class ApiAuditRecord
{
    /// <summary>
    /// Unique identifier for this audit record.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Timestamp of the request (UTC).
    /// </summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// User who made the request (from JWT 'sub' claim).
    /// Null for requests without a valid user claim.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Organization/tenant context (from JWT 'org_id' or 'tenant_id' claim).
    /// Null for requests without tenant context.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, PATCH, etc.).
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string HttpMethod { get; set; } = null!;

    /// <summary>
    /// Request path (e.g., /api/v1/servers/123).
    /// Query strings are excluded for privacy.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Path { get; set; } = null!;

    /// <summary>
    /// Resource ID extracted from path (if applicable).
    /// Extracted using pattern matching for standard resource paths.
    /// </summary>
    public Guid? ResourceId { get; set; }

    /// <summary>
    /// Resource type (e.g., "server", "node", "user", "organization").
    /// Extracted from the path segment before the resource ID.
    /// </summary>
    [MaxLength(50)]
    public string? ResourceType { get; set; }

    /// <summary>
    /// HTTP status code returned to the client.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Request duration in milliseconds.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Client IP address.
    /// Supports both IPv4 and IPv6 (max 45 chars for IPv6 + zone ID).
    /// </summary>
    [MaxLength(45)]
    public string? ClientIp { get; set; }

    /// <summary>
    /// User agent string (truncated to 256 characters).
    /// </summary>
    [MaxLength(256)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Correlation ID for distributed tracing across services.
    /// </summary>
    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// OpenTelemetry trace ID for linking with distributed traces.
    /// </summary>
    [MaxLength(32)]
    public string? TraceId { get; set; }

    /// <summary>
    /// Name of the service that processed the request.
    /// </summary>
    [MaxLength(50)]
    public string? ServiceName { get; set; }
}
