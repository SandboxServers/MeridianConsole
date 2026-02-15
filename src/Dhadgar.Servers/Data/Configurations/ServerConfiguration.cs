using Dhadgar.Servers.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerConfigEntity = Dhadgar.Servers.Data.Entities.ServerConfiguration;

namespace Dhadgar.Servers.Data.Configurations;

public sealed class ServerEntityConfiguration : IEntityTypeConfiguration<Server>
{
    public void Configure(EntityTypeBuilder<Server> builder)
    {
        builder.ToTable("servers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.DisplayName)
            .HasMaxLength(200);

        builder.Property(s => s.GameType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.Status)
            .HasConversion<int>();

        builder.Property(s => s.PowerState)
            .HasConversion<int>();

        // Tags stored as JSONB array in PostgreSQL
        builder.Property(s => s.Tags)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        // Composite index for organization queries filtered by status
        builder.HasIndex(s => new { s.OrganizationId, s.Status })
            .HasDatabaseName("ix_servers_org_status");

        // Index for node-based queries
        builder.HasIndex(s => s.NodeId)
            .HasDatabaseName("ix_servers_node");

        // Index for game type filtering
        builder.HasIndex(s => s.GameType)
            .HasDatabaseName("ix_servers_gametype");

        // Unique name within organization (for active servers only)
        builder.HasIndex(s => new { s.OrganizationId, s.Name })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_servers_org_name_unique");

        // Soft delete filter index
        builder.HasIndex(s => s.DeletedAt)
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_servers_active");

        // Global query filter for soft delete
        builder.HasQueryFilter(s => s.DeletedAt == null);

        // One-to-one relationship with configuration
        builder.HasOne(s => s.Configuration)
            .WithOne(c => c.Server)
            .HasForeignKey<ServerConfigEntity>(c => c.ServerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Many-to-one relationship with template
        builder.HasOne(s => s.Template)
            .WithMany(t => t.Servers)
            .HasForeignKey(s => s.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        // One-to-many for ports
        builder.HasMany(s => s.Ports)
            .WithOne(p => p.Server)
            .HasForeignKey(p => p.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
