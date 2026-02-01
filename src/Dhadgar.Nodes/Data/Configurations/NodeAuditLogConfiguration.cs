using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Nodes.Data.Configurations;

public sealed class NodeAuditLogConfiguration : IEntityTypeConfiguration<NodeAuditLog>
{
    public void Configure(EntityTypeBuilder<NodeAuditLog> builder)
    {
        builder.ToTable("node_audit_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Timestamp)
            .IsRequired();

        builder.Property(a => a.ActorId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.ActorType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.ResourceType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(a => a.ResourceName)
            .HasMaxLength(200);

        builder.Property(a => a.Outcome)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(a => a.FailureReason)
            .HasMaxLength(500);

        builder.Property(a => a.Details)
            .HasColumnType("jsonb");

        builder.Property(a => a.CorrelationId)
            .HasMaxLength(100);

        builder.Property(a => a.RequestId)
            .HasMaxLength(100);

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45); // IPv6 max length

        builder.Property(a => a.UserAgent)
            .HasMaxLength(500);

        // Index for time-range queries (most common SIEM query pattern)
        builder.HasIndex(a => a.Timestamp)
            .HasDatabaseName("ix_audit_timestamp")
            .IsDescending();

        // Index for actor lookups (who did what)
        builder.HasIndex(a => a.ActorId)
            .HasDatabaseName("ix_audit_actor");

        // Index for action filtering (what happened)
        builder.HasIndex(a => a.Action)
            .HasDatabaseName("ix_audit_action");

        // Index for resource lookups (what was affected)
        builder.HasIndex(a => a.ResourceId)
            .HasDatabaseName("ix_audit_resource_id");

        // Composite index for organization + timestamp (tenant-scoped time queries)
        builder.HasIndex(a => new { a.OrganizationId, a.Timestamp })
            .HasDatabaseName("ix_audit_org_timestamp")
            .IsDescending(false, true);

        // Index for correlation ID lookups (distributed tracing)
        builder.HasIndex(a => a.CorrelationId)
            .HasDatabaseName("ix_audit_correlation");

        // Index for outcome filtering (finding failures/denials)
        builder.HasIndex(a => a.Outcome)
            .HasDatabaseName("ix_audit_outcome");
    }
}
