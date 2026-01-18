using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.TargetType)
            .HasMaxLength(50);

        builder.Property(e => e.ClientIp)
            .HasMaxLength(45);

        builder.Property(e => e.UserAgent)
            .HasMaxLength(500);

        builder.Property(e => e.CorrelationId)
            .HasMaxLength(50);

        // Index for querying by user
        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("ix_audit_events_user_id");

        // Index for querying by organization
        builder.HasIndex(e => e.OrganizationId)
            .HasDatabaseName("ix_audit_events_organization_id");

        // Index for querying by event type
        builder.HasIndex(e => e.EventType)
            .HasDatabaseName("ix_audit_events_event_type");

        // Index for time-based queries and retention
        builder.HasIndex(e => e.OccurredAtUtc)
            .HasDatabaseName("ix_audit_events_occurred_at");

        // Composite index for common query pattern: org + time
        builder.HasIndex(e => new { e.OrganizationId, e.OccurredAtUtc })
            .HasDatabaseName("ix_audit_events_org_time");

        // Composite index for user activity queries
        builder.HasIndex(e => new { e.UserId, e.OccurredAtUtc })
            .HasDatabaseName("ix_audit_events_user_time");

        // No query filter - audit events should never be filtered out
    }
}
