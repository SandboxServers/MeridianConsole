using Dhadgar.Mods.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Mods.Data.Configurations;

public sealed class ModVersionConfiguration : IEntityTypeConfiguration<ModVersion>
{
    public void Configure(EntityTypeBuilder<ModVersion> builder)
    {
        builder.ToTable("mod_versions");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Version)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(v => v.Prerelease)
            .HasMaxLength(50);

        builder.Property(v => v.BuildMetadata)
            .HasMaxLength(100);

        builder.Property(v => v.ReleaseNotes)
            .HasMaxLength(10000);

        builder.Property(v => v.FileHash)
            .HasMaxLength(64);

        builder.Property(v => v.FilePath)
            .HasMaxLength(500);

        builder.Property(v => v.MinGameVersion)
            .HasMaxLength(50);

        builder.Property(v => v.MaxGameVersion)
            .HasMaxLength(50);

        builder.Property(v => v.DeprecationReason)
            .HasMaxLength(500);

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(v => v.RowVersion)
            .IsRowVersion();

        // Index for mod lookup
        builder.HasIndex(v => v.ModId)
            .HasDatabaseName("ix_mod_versions_mod");

        // Index for latest version queries - unique to enforce single latest per mod
        builder.HasIndex(v => new { v.ModId, v.IsLatest })
            .IsUnique()
            .HasFilter("\"IsLatest\" = true")
            .HasDatabaseName("ix_mod_versions_latest");

        // Index for semver sorting
        builder.HasIndex(v => new { v.ModId, v.Major, v.Minor, v.Patch })
            .HasDatabaseName("ix_mod_versions_semver");

        // Unique version per mod (for active versions only)
        builder.HasIndex(v => new { v.ModId, v.Version })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_mod_versions_mod_version_unique");

        // Global query filter for soft delete
        builder.HasQueryFilter(v => v.DeletedAt == null);

        // Dependencies relationship
        builder.HasMany(v => v.Dependencies)
            .WithOne(d => d.ModVersion)
            .HasForeignKey(d => d.ModVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Incompatibilities relationship
        builder.HasMany(v => v.Incompatibilities)
            .WithOne(c => c.ModVersion)
            .HasForeignKey(c => c.ModVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
