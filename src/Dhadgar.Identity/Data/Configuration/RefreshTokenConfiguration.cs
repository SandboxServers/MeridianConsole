using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(rt => rt.DeviceInfo)
            .HasMaxLength(500);

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        builder.HasIndex(rt => new { rt.UserId, rt.ExpiresAt })
            .HasFilter("\"RevokedAt\" IS NULL")
            .HasDatabaseName("ix_refresh_tokens_user_active");

        builder.HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rt => rt.Organization)
            .WithMany()
            .HasForeignKey(rt => rt.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
