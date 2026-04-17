using Dhadgar.Mods.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Mods.Data.Configurations;

public sealed class ModCompatibilityConfiguration : IEntityTypeConfiguration<ModCompatibility>
{
    public void Configure(EntityTypeBuilder<ModCompatibility> builder)
    {
        builder.ToTable("mod_compatibilities");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.MinVersion)
            .HasMaxLength(100);

        builder.Property(c => c.MaxVersion)
            .HasMaxLength(100);

        builder.Property(c => c.Reason)
            .HasMaxLength(1000);

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(c => c.RowVersion)
            .IsRowVersion();

        // Index for version lookup
        builder.HasIndex(c => c.ModVersionId)
            .HasDatabaseName("ix_mod_compatibilities_version");

        // Index for finding incompatible mods
        builder.HasIndex(c => c.IncompatibleWithModId)
            .HasDatabaseName("ix_mod_compatibilities_incompatible_with");

        // Unique incompatibility per version (for active records only)
        builder.HasIndex(c => new { c.ModVersionId, c.IncompatibleWithModId })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_mod_compatibilities_version_mod_unique");

        // Global query filter for soft delete
        builder.HasQueryFilter(c => c.DeletedAt == null);

        // IncompatibleWithMod relationship
        builder.HasOne(c => c.IncompatibleWithMod)
            .WithMany()
            .HasForeignKey(c => c.IncompatibleWithModId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
