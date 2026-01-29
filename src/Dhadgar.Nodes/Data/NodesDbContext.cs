using System.Text.Json;
using Dhadgar.Nodes.Data.Configurations;
using Dhadgar.Nodes.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dhadgar.Nodes.Data;

public sealed class NodesDbContext : DbContext
{
    public NodesDbContext(DbContextOptions<NodesDbContext> options) : base(options) { }

    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<NodeHardwareInventory> HardwareInventories => Set<NodeHardwareInventory>();
    public DbSet<NodeHealth> NodeHealths => Set<NodeHealth>();
    public DbSet<NodeCapacity> NodeCapacities => Set<NodeCapacity>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    public DbSet<AgentCertificate> AgentCertificates => Set<AgentCertificate>();
    public DbSet<NodeAuditLog> AuditLogs => Set<NodeAuditLog>();
    public DbSet<CapacityReservation> CapacityReservations => Set<CapacityReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations
        modelBuilder.ApplyConfiguration(new NodeConfiguration());
        modelBuilder.ApplyConfiguration(new NodeHardwareInventoryConfiguration());
        modelBuilder.ApplyConfiguration(new NodeHealthConfiguration());
        modelBuilder.ApplyConfiguration(new NodeCapacityConfiguration());
        modelBuilder.ApplyConfiguration(new EnrollmentTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AgentCertificateConfiguration());
        modelBuilder.ApplyConfiguration(new NodeAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new CapacityReservationConfiguration());

        // Add MassTransit outbox entities for transactional messaging
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();

        // Handle InMemory and SQLite providers that can't handle PostgreSQL-specific features
        if (Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true ||
            Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Remove JSONB column types for non-PostgreSQL providers
            modelBuilder.Entity<NodeHardwareInventory>()
                .Property(h => h.NetworkInterfaces)
                .HasColumnType(null);

            modelBuilder.Entity<NodeHealth>()
                .Property(h => h.HealthIssues)
                .HasColumnType(null);

            // SQLite doesn't support PostgreSQL's xmin row version column
            modelBuilder.Entity<Node>().Property(n => n.RowVersion)
                .HasDefaultValue(0u)
                .ValueGeneratedOnAddOrUpdate();

            // Remove JSONB column type for audit logs
            modelBuilder.Entity<NodeAuditLog>()
                .Property(a => a.Details)
                .HasColumnType(null);

            // Use JSON value converter for node tags (SQLite/InMemory don't support JSONB)
            var tagsConverter = new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            modelBuilder.Entity<Node>()
                .Property(n => n.Tags)
                .HasColumnType(null)
                .HasConversion(tagsConverter)
                .HasDefaultValueSql("'[]'");
        }
    }
}
