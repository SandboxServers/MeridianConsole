using System.Text.Json;
using Dhadgar.Servers.Data.Configuration;
using Dhadgar.Servers.Data.Configurations;
using Dhadgar.Servers.Data.Entities;
using Dhadgar.ServiceDefaults.Audit;
using Dhadgar.Shared.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ServerConfigEntity = Dhadgar.Servers.Data.Entities.ServerConfiguration;

namespace Dhadgar.Servers.Data;

public sealed class ServersDbContext : DhadgarDbContext, IAuditDbContext
{
    public ServersDbContext(DbContextOptions<ServersDbContext> options) : base(options) { }

    public DbSet<Server> Servers => Set<Server>();
    public DbSet<ServerConfigEntity> ServerConfigurations => Set<ServerConfigEntity>();
    public DbSet<ServerTemplate> ServerTemplates => Set<ServerTemplate>();
    public DbSet<ServerPort> ServerPorts => Set<ServerPort>();

    /// <summary>
    /// API audit records for HTTP request tracking (compliance/analysis).
    /// </summary>
    public DbSet<ApiAuditRecord> ApiAuditRecords => Set<ApiAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations
        modelBuilder.ApplyConfiguration(new ServerEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ServerConfigurationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ServerTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new ServerPortConfiguration());

        // API audit record configuration with indexes
        modelBuilder.ApplyConfiguration(new ApiAuditRecordConfiguration());

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

            // Server tags: For test providers, use value converter and empty list default
            modelBuilder.Entity<Server>()
                .Property(s => s.Tags)
                .HasConversion(tagsConverter)
                .HasDefaultValue(new List<string>());

            // Clear JSONB column types for test providers
            foreach (var prop in new[] {
                modelBuilder.Entity<ServerConfigEntity>().Property(c => c.GameSettings),
                modelBuilder.Entity<ServerConfigEntity>().Property(c => c.EnvironmentVariables),
                modelBuilder.Entity<ServerTemplate>().Property(t => t.DefaultGameSettings),
                modelBuilder.Entity<ServerTemplate>().Property(t => t.DefaultEnvironmentVariables),
                modelBuilder.Entity<ServerTemplate>().Property(t => t.DefaultPorts)
            })
            {
                prop.Metadata.SetColumnType(null);
            }
        }
    }
}
