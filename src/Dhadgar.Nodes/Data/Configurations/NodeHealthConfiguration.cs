using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Nodes.Data.Configurations;

public sealed class NodeHealthConfiguration : IEntityTypeConfiguration<NodeHealth>
{
    public void Configure(EntityTypeBuilder<NodeHealth> builder)
    {
        builder.ToTable("node_healths");

        builder.HasKey(h => h.Id);

        // Store health issues as JSONB
        builder.Property(h => h.HealthIssues)
            .HasColumnType("jsonb");

        // Health score (0-100)
        builder.Property(h => h.HealthScore)
            .HasDefaultValue(100);

        // Health trend enum stored as integer
        builder.Property(h => h.HealthTrend)
            .HasDefaultValue(HealthTrend.Stable);

        // Unique index on NodeId (one health record per node)
        builder.HasIndex(h => h.NodeId)
            .IsUnique()
            .HasDatabaseName("ix_node_healths_node_id");

        // Index for finding nodes with health issues
        builder.HasIndex(h => h.ReportedAt)
            .HasDatabaseName("ix_node_healths_reported_at");

        // Index for querying by health score (useful for scheduling decisions)
        builder.HasIndex(h => h.HealthScore)
            .HasDatabaseName("ix_node_healths_score");
    }
}
