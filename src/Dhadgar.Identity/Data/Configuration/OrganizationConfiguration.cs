using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(o => o.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.Version)
            .IsRowVersion();

        builder.OwnsOne(o => o.Settings, settings =>
        {
            settings.ToJson();
        });

        builder.HasIndex(o => o.Slug)
            .IsUnique()
            .HasDatabaseName("ix_organizations_slug");

        builder.HasIndex(o => o.OwnerId)
            .HasDatabaseName("ix_organizations_owner_id");

        builder.HasIndex(o => o.DeletedAt)
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_organizations_active");

        builder.HasOne(o => o.Owner)
            .WithMany()
            .HasForeignKey(o => o.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(o => o.DeletedAt == null);
    }
}
