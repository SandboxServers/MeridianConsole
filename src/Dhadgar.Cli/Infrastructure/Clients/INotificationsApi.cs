using Refit;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Refit interface for Notifications service admin API.
/// </summary>
public interface INotificationsApi
{
    [Get("/api/v1/notifications/logs")]
    Task<IReadOnlyList<NotificationLogDto>> GetLogsAsync(
        [Header("X-Admin-Api-Key")] string apiKey,
        [Query] int? limit = null,
        [Query] string? status = null,
        [Query] Guid? orgId = null,
        [Header("X-Tenant-Id")] string? tenantId = null,
        CancellationToken ct = default);

    [Post("/api/v1/notifications/test")]
    Task<TestNotificationResponse> SendTestNotificationAsync(
        [Header("X-Admin-Api-Key")] string apiKey,
        [Body] TestNotificationRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// DTO for notification log entries.
/// </summary>
public record NotificationLogDto(
    Guid Id,
    Guid OrganizationId,
    string EventType,
    string Channel,
    string Recipient,
    string Subject,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Request payload for sending a test notification.
/// </summary>
public record TestNotificationRequest(
    Guid? OrgId,
    string? Title,
    string? Message,
    string? Severity);

/// <summary>
/// Response from sending a test notification.
/// </summary>
public record TestNotificationResponse(
    Guid NotificationId,
    string Message);
