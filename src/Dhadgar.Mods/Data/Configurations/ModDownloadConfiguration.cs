using Dhadgar.Mods.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Mods.Data.Configurations;

public sealed class ModDownloadConfiguration : IEntityTypeConfiguration<ModDownload>
{
    public void Configure(EntityTypeBuilder<ModDownload> builder)
    {
        builder.ToTable("mod_downloads");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.IpAddressHash)
            .HasMaxLength(ModDownload.IpAddressHashMaxLength);

        // Index for version analytics
        builder.HasIndex(d => d.ModVersionId)
            .HasDatabaseName("ix_mod_downloads_version");

        // Index for time-based queries
        builder.HasIndex(d => d.DownloadedAt)
            .HasDatabaseName("ix_mod_downloads_date");

        // Index for org analytics
        builder.HasIndex(d => d.OrganizationId)
            .HasDatabaseName("ix_mod_downloads_org");

        // Composite index for analytics aggregation
        builder.HasIndex(d => new { d.ModVersionId, d.DownloadedAt })
            .HasDatabaseName("ix_mod_downloads_version_date");

        // ModVersion relationship
        builder.HasOne(d => d.ModVersion)
            .WithMany()
            .HasForeignKey(d => d.ModVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
