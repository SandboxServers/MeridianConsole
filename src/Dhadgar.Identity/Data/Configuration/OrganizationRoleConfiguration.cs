using System.Collections.ObjectModel;
using System.Text.Json;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class OrganizationRoleConfiguration : IEntityTypeConfiguration<OrganizationRole>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Configure(EntityTypeBuilder<OrganizationRole> builder)
    {
        builder.ToTable("organization_roles");

        builder.HasKey(role => role.Id);

        builder.Property(role => role.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(role => role.NormalizedName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(role => role.Description)
            .HasMaxLength(500);

        var converter = new ValueConverter<Collection<string>, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => string.IsNullOrWhiteSpace(value)
                ? new Collection<string>()
                : new Collection<string>(
                    JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? new List<string>()));

        builder.Property(role => role.Permissions)
            .HasConversion(converter)
            .HasColumnType("jsonb");

        builder.HasIndex(role => new { role.OrganizationId, role.NormalizedName })
            .IsUnique()
            .HasDatabaseName("ix_org_roles_org_id_name");

        builder.HasIndex(role => role.OrganizationId)
            .HasDatabaseName("ix_org_roles_org_id");

        builder.HasOne(role => role.Organization)
            .WithMany(org => org.Roles)
            .HasForeignKey(role => role.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
