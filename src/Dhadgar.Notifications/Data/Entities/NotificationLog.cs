namespace Dhadgar.Notifications.Data.Entities;

/// <summary>
/// Audit trail of all notification delivery attempts.
/// </summary>
public sealed class NotificationLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// The organization this notification was sent for.
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Optional user ID if this was a user-specific notification.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// The event type that triggered this notification.
    /// </summary>
    public string EventType { get; set; } = null!;

    /// <summary>
    /// The delivery channel used (email, discord, push, in_app).
    /// </summary>
    public string Channel { get; set; } = null!;

    /// <summary>
    /// The notification title/subject.
    /// </summary>
    public string Title { get; set; } = null!;

    /// <summary>
    /// The notification message/body (truncated if too long).
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// The delivery status: pending, sent, failed.
    /// </summary>
    public string Status { get; set; } = NotificationStatus.Pending;

    /// <summary>
    /// Error message if delivery failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When the notification was queued.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// When the notification was successfully sent (null if not yet sent).
    /// </summary>
    public DateTimeOffset? SentAtUtc { get; set; }

    /// <summary>
    /// Related entity type (e.g., "server", "backup", "payment").
    /// </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// Related entity ID for linking back to the source.
    /// </summary>
    public Guid? RelatedEntityId { get; set; }
}

public static class NotificationStatus
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
}
