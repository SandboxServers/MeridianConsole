namespace Dhadgar.Contracts.Notifications;

/// <summary>
/// Notification channel types supported by the system.
/// </summary>
public static class NotificationChannels
{
    public const string Email = "email";
    public const string Discord = "discord";
    public const string Push = "push";
    public const string InApp = "in_app";
}

/// <summary>
/// Event types that can trigger notifications.
/// </summary>
public static class NotificationEventTypes
{
    public const string ServerStarted = "server.started";
    public const string ServerStopped = "server.stopped";
    public const string ServerCrashed = "server.crashed";
    public const string ServerRestarted = "server.restarted";
    public const string BackupCompleted = "backup.completed";
    public const string BackupFailed = "backup.failed";
    public const string PaymentReceived = "payment.received";
    public const string PaymentFailed = "payment.failed";
    public const string SubscriptionExpiring = "subscription.expiring";
    public const string ResourceWarning = "resource.warning";
    public const string NodeOffline = "node.offline";
}

/// <summary>
/// Severity levels for notifications.
/// </summary>
public static class NotificationSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}

// ============================================================================
// Commands - Sent to delivery channels
// ============================================================================

/// <summary>
/// Command to send an email notification via SendGrid.
/// </summary>
public record SendEmailNotification(
    Guid NotificationId,
    Guid OrgId,
    Guid? UserId,
    string RecipientEmail,
    string Subject,
    string HtmlBody,
    string? TextBody,
    string EventType,
    string Severity,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Command to send a Discord notification via webhook or bot.
/// </summary>
public record SendDiscordNotification(
    Guid NotificationId,
    Guid OrgId,
    Guid? ServerId,
    string Title,
    string Message,
    string Severity,
    string EventType,
    IReadOnlyDictionary<string, string>? Fields,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Command to send a push notification to a user's devices.
/// </summary>
public record SendPushNotification(
    Guid NotificationId,
    Guid UserId,
    string Title,
    string Body,
    Uri? ActionUrl,
    string EventType,
    string Severity,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Command to create an in-app notification for a user.
/// </summary>
public record CreateInAppNotification(
    Guid NotificationId,
    Guid OrgId,
    Guid UserId,
    string Title,
    string Message,
    string Severity,
    string EventType,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset OccurredAtUtc);

// ============================================================================
// Events - Published after delivery
// ============================================================================

/// <summary>
/// Event published when a notification is successfully delivered.
/// </summary>
public record NotificationDelivered(
    Guid NotificationId,
    Guid OrgId,
    Guid? UserId,
    string Channel,
    string EventType,
    DateTimeOffset DeliveredAtUtc);

/// <summary>
/// Event published when a notification delivery fails.
/// </summary>
public record NotificationDeliveryFailed(
    Guid NotificationId,
    Guid OrgId,
    Guid? UserId,
    string Channel,
    string EventType,
    string ErrorMessage,
    int RetryCount,
    DateTimeOffset FailedAtUtc);

// ============================================================================
// Additional domain events that trigger notifications
// ============================================================================

/// <summary>
/// Event when a backup completes successfully.
/// </summary>
public record BackupCompleted(
    Guid BackupId,
    Guid ServerId,
    Guid OrgId,
    string ServerName,
    string BackupName,
    long SizeBytes,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Event when a backup fails.
/// </summary>
public record BackupFailed(
    Guid BackupId,
    Guid ServerId,
    Guid OrgId,
    string ServerName,
    string ErrorMessage,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Event when a payment is received.
/// </summary>
public record PaymentReceived(
    Guid PaymentId,
    Guid OrgId,
    decimal Amount,
    string Currency,
    string? Description,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Event when a payment fails.
/// </summary>
public record PaymentFailed(
    Guid PaymentId,
    Guid OrgId,
    decimal Amount,
    string Currency,
    string ErrorMessage,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Event when a subscription is expiring soon.
/// </summary>
public record SubscriptionExpiring(
    Guid SubscriptionId,
    Guid OrgId,
    string PlanName,
    int DaysRemaining,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Event when a resource usage threshold is exceeded.
/// </summary>
public record ResourceWarning(
    Guid OrgId,
    Guid? ServerId,
    Guid? NodeId,
    string ResourceType,
    decimal CurrentUsage,
    decimal ThresholdPercent,
    string? ServerName,
    string? NodeName,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Event when a node goes offline unexpectedly.
/// </summary>
public record NodeOffline(
    Guid NodeId,
    Guid OrgId,
    string NodeName,
    string? Reason,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset OccurredAtUtc);
