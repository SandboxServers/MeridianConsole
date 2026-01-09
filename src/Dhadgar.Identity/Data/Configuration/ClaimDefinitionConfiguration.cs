using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public sealed class ClaimDefinitionConfiguration : IEntityTypeConfiguration<ClaimDefinition>
{
    public void Configure(EntityTypeBuilder<ClaimDefinition> builder)
    {
        builder.ToTable("claim_definitions");

        builder.HasKey(cd => cd.Id);

        builder.Property(cd => cd.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cd => cd.Category)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(cd => cd.Description)
            .HasMaxLength(500);

        builder.HasIndex(cd => cd.Name)
            .IsUnique()
            .HasDatabaseName("ix_claim_definitions_name");

        builder.HasIndex(cd => cd.Category)
            .HasDatabaseName("ix_claim_definitions_category");
    }
}
