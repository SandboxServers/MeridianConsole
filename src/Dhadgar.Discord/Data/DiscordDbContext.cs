using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Discord.Data;

/// <summary>
/// Database context for Discord service.
/// Minimal schema for internal admin use - just tracks notification history.
/// </summary>
public sealed class DiscordDbContext : DbContext
{
    public DiscordDbContext(DbContextOptions<DiscordDbContext> options) : base(options) { }

    /// <summary>
    /// Log of notifications sent to Discord for debugging/audit.
    /// </summary>
    public DbSet<DiscordNotificationLog> NotificationLogs => Set<DiscordNotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DiscordNotificationLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EventType).HasMaxLength(100);
            b.Property(x => x.Channel).HasMaxLength(100);
            b.Property(x => x.Title).HasMaxLength(500);
            b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.ErrorMessage).HasMaxLength(1000);

            b.HasIndex(x => x.CreatedAtUtc);
            b.HasIndex(x => new { x.EventType, x.Status });
        });
    }
}

/// <summary>
/// Log entry for a Discord notification.
/// </summary>
public sealed class DiscordNotificationLog
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Status { get; set; } = null!; // sent, failed
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
