using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.UserName)
            .HasMaxLength(256);

        builder.Property(u => u.NormalizedUserName)
            .HasMaxLength(256);

        builder.Property(u => u.NormalizedEmail)
            .HasMaxLength(320);

        builder.Property(u => u.ExternalAuthId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(u => u.DisplayName)
            .HasMaxLength(200);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(320);

        // PostgreSQL xmin-based optimistic concurrency via Npgsql 7.0+ convention:
        // When IsRowVersion() is used on a uint property, Npgsql automatically maps it to PostgreSQL's xmin system column.
        // This is PostgreSQL-native and avoids the SQL Server byte[] rowversion pattern.
        builder.Property(u => u.Version)
            .IsRowVersion();

        builder.HasIndex(u => u.ExternalAuthId)
            .IsUnique()
            .HasDatabaseName("ix_users_external_auth_id");

        builder.HasIndex(u => u.Email)
            .HasDatabaseName("ix_users_email");

        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ix_users_normalized_email");

        builder.HasIndex(u => u.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ix_users_normalized_username");

        builder.HasIndex(u => u.DeletedAt)
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("ix_users_active");

        builder.HasOne(u => u.PreferredOrganization)
            .WithMany()
            .HasForeignKey(u => u.PreferredOrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}
