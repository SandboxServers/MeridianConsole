using System.Text.Json;
using Dhadgar.Nodes.Data.Configurations;
using Dhadgar.Nodes.Data.Entities;
using Dhadgar.Shared.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dhadgar.Nodes.Data;

public sealed class NodesDbContext : DhadgarDbContext
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

        // Apply base class conventions (soft-delete filters and provider-specific handling)
        ApplySoftDeleteConventions(modelBuilder);
        ApplyProviderSpecificConventions(modelBuilder);

        // Handle additional test provider-specific configurations not covered by base class
        if (IsTestProvider)
        {
            // Use JSON value converter for node tags (SQLite/InMemory don't support JSONB)
            var tagsConverter = new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            // Note: HasDefaultValue with a List<string> is safe here because EF Core
            // creates new instances for each entity, not reusing the reference.
            // The ValueConverter handles serialization to/from JSON for storage.
            // Must clear HasDefaultValueSql (set by NodeConfiguration) before setting HasDefaultValue.
            modelBuilder.Entity<Node>()
                .Property(n => n.Tags)
                .HasConversion(tagsConverter)
                .HasDefaultValueSql(null)
                .HasDefaultValue(new List<string>());
        }
    }
}
