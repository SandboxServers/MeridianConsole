using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerConfigEntity = Dhadgar.Servers.Data.Entities.ServerConfiguration;

namespace Dhadgar.Servers.Data.Configurations;

public sealed class ServerConfigurationEntityConfiguration : IEntityTypeConfiguration<ServerConfigEntity>
{
    public void Configure(EntityTypeBuilder<ServerConfigEntity> builder)
    {
        builder.ToTable("server_configurations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.StartupCommand)
            .HasMaxLength(2000);

        builder.Property(c => c.GameSettings)
            .HasColumnType("jsonb");

        builder.Property(c => c.EnvironmentVariables)
            .HasColumnType("jsonb");

        builder.Property(c => c.JavaFlags)
            .HasMaxLength(500);

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        // Index for server lookup
        builder.HasIndex(c => c.ServerId)
            .IsUnique()
            .HasDatabaseName("ix_server_configurations_server");
    }
}
