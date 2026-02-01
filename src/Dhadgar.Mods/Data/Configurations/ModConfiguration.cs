using Dhadgar.Mods.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Mods.Data.Configurations;

public sealed class ModConfiguration : IEntityTypeConfiguration<Mod>
{
    public void Configure(EntityTypeBuilder<Mod> builder)
    {
        builder.ToTable("mods");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.Description)
            .HasMaxLength(4000);

        builder.Property(m => m.Author)
            .HasMaxLength(200);

        builder.Property(m => m.GameType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(m => m.ProjectUrl)
            .HasMaxLength(500);

        builder.Property(m => m.IconUrl)
            .HasMaxLength(500);

        // Tags stored as JSONB array in PostgreSQL
        builder.Property(m => m.Tags)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        // PostgreSQL xmin-based optimistic concurrency
        builder.Property(m => m.RowVersion)
            .IsRowVersion();

        // Index for organization queries
        builder.HasIndex(m => m.OrganizationId)
            .HasDatabaseName("ix_mods_org");

        // Index for public mod discovery
        builder.HasIndex(m => m.IsPublic)
            .HasFilter("\"IsPublic\" = true AND \"IsArchived\" = false")
            .HasDatabaseName("ix_mods_public");

        // Index for game type filtering
        builder.HasIndex(m => m.GameType)
            .HasDatabaseName("ix_mods_gametype");

        // Index for category
        builder.HasIndex(m => m.CategoryId)
            .HasDatabaseName("ix_mods_category");

        // Index for popular mods
        builder.HasIndex(m => m.TotalDownloads)
            .HasDatabaseName("ix_mods_downloads");

        // Unique slug within organization (for active mods only)
        builder.HasIndex(m => new { m.OrganizationId, m.Slug })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_mods_org_slug_unique");

        // Global query filter for soft delete
        builder.HasQueryFilter(m => m.DeletedAt == null);

        // Category relationship
        builder.HasOne(m => m.Category)
            .WithMany(c => c.Mods)
            .HasForeignKey(m => m.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Versions relationship
        builder.HasMany(m => m.Versions)
            .WithOne(v => v.Mod)
            .HasForeignKey(v => v.ModId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
