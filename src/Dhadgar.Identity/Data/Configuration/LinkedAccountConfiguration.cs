using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class LinkedAccountConfiguration : IEntityTypeConfiguration<LinkedAccount>
{
    public void Configure(EntityTypeBuilder<LinkedAccount> builder)
    {
        builder.ToTable("linked_accounts");

        builder.HasKey(la => la.Id);

        builder.Property(la => la.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(la => la.ProviderAccountId)
            .IsRequired()
            .HasMaxLength(255);

        builder.OwnsOne(la => la.ProviderMetadata, metadata =>
        {
            metadata.ToJson();
        });

        builder.HasIndex(la => new { la.Provider, la.ProviderAccountId })
            .IsUnique()
            .HasDatabaseName("ix_linked_accounts_provider_account");

        builder.HasIndex(la => la.UserId)
            .HasDatabaseName("ix_linked_accounts_user_id");

        builder.HasOne(la => la.User)
            .WithMany(u => u.LinkedAccounts)
            .HasForeignKey(la => la.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
