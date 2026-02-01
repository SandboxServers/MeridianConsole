using System.Text.Json;
using Dhadgar.Mods.Data.Configurations;
using Dhadgar.Mods.Data.Entities;
using Dhadgar.Shared.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dhadgar.Mods.Data;

public sealed class ModsDbContext : DhadgarDbContext
{
    public ModsDbContext(DbContextOptions<ModsDbContext> options) : base(options) { }

    public DbSet<Mod> Mods => Set<Mod>();
    public DbSet<ModVersion> ModVersions => Set<ModVersion>();
    public DbSet<ModDependency> ModDependencies => Set<ModDependency>();
    public DbSet<ModCompatibility> ModCompatibilities => Set<ModCompatibility>();
    public DbSet<ModDownload> ModDownloads => Set<ModDownload>();
    public DbSet<ModCategory> ModCategories => Set<ModCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations
        modelBuilder.ApplyConfiguration(new ModConfiguration());
        modelBuilder.ApplyConfiguration(new ModVersionConfiguration());
        modelBuilder.ApplyConfiguration(new ModDependencyConfiguration());
        modelBuilder.ApplyConfiguration(new ModCompatibilityConfiguration());
        modelBuilder.ApplyConfiguration(new ModDownloadConfiguration());
        modelBuilder.ApplyConfiguration(new ModCategoryConfiguration());

        // Add MassTransit outbox entities for transactional messaging
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();

        // Apply base class conventions (soft-delete filters and provider-specific handling)
        ApplySoftDeleteConventions(modelBuilder);
        ApplyProviderSpecificConventions(modelBuilder);

        // Handle additional test provider-specific configurations not covered by base class
        if (IsTestProvider)
        {
            // Use JSON value converter for tags (SQLite/InMemory don't support JSONB)
            var tagsConverter = new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            modelBuilder.Entity<Mod>()
                .Property(m => m.Tags)
                .HasConversion(tagsConverter)
                .HasDefaultValueSql(null)
                .HasDefaultValue(new List<string>());
        }
    }
}
