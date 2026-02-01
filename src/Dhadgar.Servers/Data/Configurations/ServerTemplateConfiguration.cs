using Dhadgar.Servers.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Servers.Data.Configurations;

public sealed class ServerTemplateConfiguration : IEntityTypeConfiguration<ServerTemplate>
{
    public void Configure(EntityTypeBuilder<ServerTemplate> builder)
    {
        builder.ToTable("server_templates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.Description)
            .HasMaxLength(1000);

        builder.Property(t => t.GameType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.DefaultStartupCommand)
            .HasMaxLength(2000);

        builder.Property(t => t.DefaultGameSettings)
            .HasColumnType("jsonb");

        builder.Property(t => t.DefaultEnvironmentVariables)
            .HasColumnType("jsonb");

        builder.Property(t => t.DefaultJavaFlags)
            .HasMaxLength(500);

        builder.Property(t => t.DefaultPorts)
            .HasColumnType("jsonb");

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        // Index for organization queries
        builder.HasIndex(t => t.OrganizationId)
            .HasDatabaseName("ix_server_templates_org");

        // Index for public templates
        builder.HasIndex(t => t.IsPublic)
            .HasFilter("\"IsPublic\" = true AND \"IsArchived\" = false")
            .HasDatabaseName("ix_server_templates_public");

        // Index for game type
        builder.HasIndex(t => t.GameType)
            .HasDatabaseName("ix_server_templates_gametype");

        // Unique name within organization (for active templates only)
        builder.HasIndex(t => new { t.OrganizationId, t.Name })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_server_templates_org_name_unique");

        // Global query filter for soft delete
        builder.HasQueryFilter(t => t.DeletedAt == null);
    }
}
