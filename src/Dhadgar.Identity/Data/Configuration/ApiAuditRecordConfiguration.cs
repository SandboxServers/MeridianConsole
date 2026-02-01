using Dhadgar.ServiceDefaults.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

/// <summary>
/// EF Core configuration for the API audit records table.
/// </summary>
/// <remarks>
/// <para>
/// This table stores HTTP request audit records (per AUDIT-01) and is distinct from
/// <see cref="AuditEventConfiguration"/> which configures domain-level audit events
/// (user.created, role.assigned, etc.).
/// </para>
/// <para>
/// Defines indexes optimized for common audit query patterns:
/// </para>
/// <list type="bullet">
///   <item><c>ix_audit_user_time</c>: User activity queries ("show me what user X did")</item>
///   <item><c>ix_audit_tenant_time</c>: Tenant/org queries ("show me all activity for org Y")</item>
///   <item><c>ix_audit_timestamp</c>: Time-based cleanup and range queries</item>
///   <item><c>ix_audit_resource_time</c>: Resource-specific queries ("who touched user Z")</item>
/// </list>
/// </remarks>
public sealed class ApiAuditRecordConfiguration : IEntityTypeConfiguration<ApiAuditRecord>
{
    public void Configure(EntityTypeBuilder<ApiAuditRecord> builder)
    {
        builder.ToTable("api_audit_records");
        builder.HasKey(e => e.Id);

        // Primary query patterns with composite indexes
        // User activity: "Show me what user X did between time A and B"
        builder.HasIndex(e => new { e.UserId, e.TimestampUtc })
            .HasDatabaseName("ix_audit_user_time")
            .IsDescending(false, true);

        // Tenant activity: "Show me all activity for org Y between time A and B"
        builder.HasIndex(e => new { e.TenantId, e.TimestampUtc })
            .HasDatabaseName("ix_audit_tenant_time")
            .IsDescending(false, true);

        // Cleanup index: Used by AuditCleanupService for 90-day retention
        builder.HasIndex(e => e.TimestampUtc)
            .HasDatabaseName("ix_audit_timestamp");

        // Resource lookup: "Who accessed user/organization Z and when"
        builder.HasIndex(e => new { e.ResourceType, e.ResourceId, e.TimestampUtc })
            .HasDatabaseName("ix_audit_resource_time")
            .IsDescending(false, false, true);
    }
}
