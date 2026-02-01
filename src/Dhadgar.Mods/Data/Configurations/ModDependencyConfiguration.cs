using Dhadgar.Mods.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Mods.Data.Configurations;

public sealed class ModDependencyConfiguration : IEntityTypeConfiguration<ModDependency>
{
    public void Configure(EntityTypeBuilder<ModDependency> builder)
    {
        builder.ToTable("mod_dependencies");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.MinVersion)
            .HasMaxLength(100);

        builder.Property(d => d.MaxVersion)
            .HasMaxLength(100);

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(d => d.RowVersion)
            .IsRowVersion();

        // Index for version lookup
        builder.HasIndex(d => d.ModVersionId)
            .HasDatabaseName("ix_mod_dependencies_version");

        // Index for finding dependents
        builder.HasIndex(d => d.DependsOnModId)
            .HasDatabaseName("ix_mod_dependencies_depends_on");

        // Unique dependency per version (for active dependencies only)
        builder.HasIndex(d => new { d.ModVersionId, d.DependsOnModId })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_mod_dependencies_version_mod_unique");

        // Global query filter for soft delete
        builder.HasQueryFilter(d => d.DeletedAt == null);

        // DependsOnMod relationship
        builder.HasOne(d => d.DependsOnMod)
            .WithMany()
            .HasForeignKey(d => d.DependsOnModId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
