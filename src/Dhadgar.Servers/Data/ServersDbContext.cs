using Dhadgar.Servers.Data.Configuration;
using Dhadgar.ServiceDefaults.Audit;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Servers.Data;

public sealed class ServersDbContext : DbContext, IAuditDbContext
{
    public ServersDbContext(DbContextOptions<ServersDbContext> options) : base(options) { }

    // TODO: Replace with real entities
    public DbSet<SampleEntity> Sample => Set<SampleEntity>();

    /// <summary>
    /// API audit records for HTTP request tracking (compliance/analysis).
    /// </summary>
    public DbSet<ApiAuditRecord> ApiAuditRecords => Set<ApiAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SampleEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
        });

        // API audit record configuration with indexes
        modelBuilder.ApplyConfiguration(new ApiAuditRecordConfiguration());
    }
}

public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
