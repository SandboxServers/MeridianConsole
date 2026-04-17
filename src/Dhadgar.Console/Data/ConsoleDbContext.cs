using Dhadgar.Console.Data.Configurations;
using Dhadgar.Console.Data.Entities;
using Dhadgar.Shared.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Console.Data;

public sealed class ConsoleDbContext : DhadgarDbContext
{
    public ConsoleDbContext(DbContextOptions<ConsoleDbContext> options) : base(options) { }

    public DbSet<ConsoleHistoryEntry> ConsoleHistory => Set<ConsoleHistoryEntry>();
    public DbSet<CommandAuditLog> CommandAuditLogs => Set<CommandAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations
        modelBuilder.ApplyConfiguration(new ConsoleHistoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CommandAuditLogConfiguration());

        // Add MassTransit outbox entities for transactional messaging
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();

        // Apply base class conventions (soft-delete filters and provider-specific handling)
        ApplySoftDeleteConventions(modelBuilder);
        ApplyProviderSpecificConventions(modelBuilder);
    }
}
