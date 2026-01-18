using System.Text.Json;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

        // Use value converter instead of OwnsOne/ToJson to avoid EF Core 10 bugs
        builder.Property(o => o.Settings)
            .HasColumnName("Settings")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<OrganizationSettings>(v, JsonOptions) ?? new OrganizationSettings())
            .Metadata.SetValueComparer(new ValueComparer<OrganizationSettings>(
                (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                v => JsonSerializer.Deserialize<OrganizationSettings>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));

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
