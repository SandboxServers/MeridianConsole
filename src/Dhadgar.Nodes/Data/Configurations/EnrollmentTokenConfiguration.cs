using Dhadgar.Nodes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Nodes.Data.Configurations;

public sealed class EnrollmentTokenConfiguration : IEntityTypeConfiguration<EnrollmentToken>
{
    public void Configure(EntityTypeBuilder<EnrollmentToken> builder)
    {
        builder.ToTable("enrollment_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(t => t.Label)
            .HasMaxLength(200);

        builder.Property(t => t.CreatedByUserId)
            .IsRequired()
            .HasMaxLength(100);

        // Unique index on token hash for fast lookup
        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_enrollment_tokens_hash");

        // Index for finding active tokens by organization
        builder.HasIndex(t => new { t.OrganizationId, t.ExpiresAt })
            .HasDatabaseName("ix_enrollment_tokens_org_expires");

        // Index for cleanup of expired tokens
        builder.HasIndex(t => t.ExpiresAt)
            .HasDatabaseName("ix_enrollment_tokens_expires");
    }
}
