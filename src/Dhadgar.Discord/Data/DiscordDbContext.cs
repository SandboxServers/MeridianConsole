using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Discord.Data;

/// <summary>
/// Status values for notification logs.
/// </summary>
public static class NotificationStatus
{
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Pending = "pending";
}

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
            b.HasIndex(x => x.OrganizationId);
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

    /// <summary>
    /// The organization this notification was sent for (for tenant isolation).
    /// </summary>
    public Guid OrganizationId { get; set; }

    public string EventType { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public string Title { get; set; } = null!;
    /// <summary>
    /// Status of the notification. Use <see cref="NotificationStatus"/> constants.
    /// </summary>
    public string Status { get; set; } = null!;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
