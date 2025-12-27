using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Tasks.Data;

public sealed class TasksDbContext : DbContext
{
    public TasksDbContext(DbContextOptions<TasksDbContext> options) : base(options) { }

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
