using Dhadgar.Servers.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Servers.Data.Configurations;

public sealed class ServerPortConfiguration : IEntityTypeConfiguration<ServerPort>
{
    public void Configure(EntityTypeBuilder<ServerPort> builder)
    {
        builder.ToTable("server_ports");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Protocol)
            .IsRequired()
            .HasMaxLength(10);

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        // Index for server lookup
        builder.HasIndex(p => p.ServerId)
            .HasDatabaseName("ix_server_ports_server");

        // Unique port name per server
        builder.HasIndex(p => new { p.ServerId, p.Name })
            .IsUnique()
            .HasDatabaseName("ix_server_ports_server_name_unique");

        // Unique external port per server (prevent port conflicts)
        builder.HasIndex(p => new { p.ServerId, p.ExternalPort })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_server_ports_server_externalport_unique");

        // Global query filter for soft delete
        builder.HasQueryFilter(p => p.DeletedAt == null);
    }
}
