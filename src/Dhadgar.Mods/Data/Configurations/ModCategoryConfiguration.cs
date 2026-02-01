using Dhadgar.Mods.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Mods.Data.Configurations;

public sealed class ModCategoryConfiguration : IEntityTypeConfiguration<ModCategory>
{
    public void Configure(EntityTypeBuilder<ModCategory> builder)
    {
        builder.ToTable("mod_categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.Slug)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.Icon)
            .HasMaxLength(200);

        // Unique slug
        builder.HasIndex(c => c.Slug)
            .IsUnique()
            .HasDatabaseName("ix_mod_categories_slug_unique");

        // Index for sorting
        builder.HasIndex(c => c.SortOrder)
            .HasDatabaseName("ix_mod_categories_sort");

        // Parent category relationship
        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
