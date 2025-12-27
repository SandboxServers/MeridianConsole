using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Notifications.Data;

public sealed class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : base(options) { }

    // TODO: Replace with real entities
    public DbSet<SampleEntity> Sample => Set<SampleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SampleEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
        });
    }
}

public sealed class SampleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "hello";
}
