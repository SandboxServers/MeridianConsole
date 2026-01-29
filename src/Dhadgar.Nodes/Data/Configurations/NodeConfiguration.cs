using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Nodes.Data.Configurations;

public sealed class NodeConfiguration : IEntityTypeConfiguration<Node>
{
    public void Configure(EntityTypeBuilder<Node> builder)
    {
        builder.ToTable("nodes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(n => n.DisplayName)
            .HasMaxLength(200);

        builder.Property(n => n.Platform)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(n => n.AgentVersion)
            .HasMaxLength(50);

        builder.Property(n => n.Status)
            .HasConversion<int>();

        // Tags stored as JSONB array in PostgreSQL
        builder.Property(n => n.Tags)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(n => n.RowVersion)
            .IsRowVersion();

        // Composite index for organization queries filtered by status
        builder.HasIndex(n => new { n.OrganizationId, n.Status })
            .HasDatabaseName("ix_nodes_org_status");

        // Index for heartbeat monitoring (finding stale nodes)
        builder.HasIndex(n => n.LastHeartbeat)
            .HasDatabaseName("ix_nodes_last_heartbeat");

        // Unique name within organization (for active nodes only)
        builder.HasIndex(n => new { n.OrganizationId, n.Name })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_nodes_org_name_unique");

        // Soft delete filter index
        builder.HasIndex(n => n.DeletedAt)
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_nodes_active");

        // Global query filter for soft delete
        builder.HasQueryFilter(n => n.DeletedAt == null);

        // One-to-one relationships
        builder.HasOne(n => n.HardwareInventory)
            .WithOne(h => h.Node)
            .HasForeignKey<NodeHardwareInventory>(h => h.NodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.Health)
            .WithOne(h => h.Node)
            .HasForeignKey<NodeHealth>(h => h.NodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.Capacity)
            .WithOne(c => c.Node)
            .HasForeignKey<NodeCapacity>(c => c.NodeId)
            .OnDelete(DeleteBehavior.Cascade);

        // One-to-many for certificates
        builder.HasMany(n => n.Certificates)
            .WithOne(c => c.Node)
            .HasForeignKey(c => c.NodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
