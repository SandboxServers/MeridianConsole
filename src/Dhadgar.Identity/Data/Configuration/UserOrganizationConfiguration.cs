using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class UserOrganizationConfiguration : IEntityTypeConfiguration<UserOrganization>
{
    public void Configure(EntityTypeBuilder<UserOrganization> builder)
    {
        builder.ToTable("user_organizations");

        builder.HasKey(uo => uo.Id);

        builder.Property(uo => uo.Role)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("viewer");

        builder.HasIndex(uo => new { uo.UserId, uo.OrganizationId })
            .HasFilter("\"LeftAt\" IS NULL")
            .IsUnique()
            .HasDatabaseName("ix_user_organizations_active_membership");

        builder.HasIndex(uo => uo.OrganizationId)
            .HasDatabaseName("ix_user_organizations_organization_id");

        builder.HasOne(uo => uo.User)
            .WithMany(u => u.Organizations)
            .HasForeignKey(uo => uo.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(uo => uo.Organization)
            .WithMany(o => o.Members)
            .HasForeignKey(uo => uo.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(uo => uo.InvitedBy)
            .WithMany()
            .HasForeignKey(uo => uo.InvitedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
