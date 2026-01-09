using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class UserOrganizationClaimConfiguration : IEntityTypeConfiguration<UserOrganizationClaim>
{
    public void Configure(EntityTypeBuilder<UserOrganizationClaim> builder)
    {
        builder.ToTable("user_organization_claims");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ClaimValue)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.ResourceType)
            .HasMaxLength(50);

        builder.HasIndex(c => c.UserOrganizationId)
            .HasDatabaseName("ix_user_org_claims_user_org_id");

        builder.HasIndex(c => new { c.UserOrganizationId, c.ClaimValue })
            .HasDatabaseName("ix_user_org_claims_lookup");

        builder.HasOne(c => c.UserOrganization)
            .WithMany(uo => uo.CustomClaims)
            .HasForeignKey(c => c.UserOrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.GrantedBy)
            .WithMany()
            .HasForeignKey(c => c.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
