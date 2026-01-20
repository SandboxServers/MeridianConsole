using Dhadgar.Notifications.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Notifications.Data;

/// <summary>
/// Database context for the Notifications service.
/// Stores logs of all dispatched notifications for audit/debugging.
/// </summary>
public sealed class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : base(options) { }

    public DbSet<NotificationLog> Logs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<NotificationLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EventType).HasMaxLength(100);
            b.Property(x => x.Channel).HasMaxLength(50);
            b.Property(x => x.Title).HasMaxLength(500);
            b.Property(x => x.Message).HasMaxLength(4000);
            b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.ErrorMessage).HasMaxLength(2000);
            b.Property(x => x.RelatedEntityType).HasMaxLength(50);

            // Index for querying logs by org and status
            b.HasIndex(x => new { x.OrganizationId, x.Status, x.CreatedAtUtc });

            // Index for querying logs by channel
            b.HasIndex(x => new { x.Channel, x.Status });
        });
    }
}
