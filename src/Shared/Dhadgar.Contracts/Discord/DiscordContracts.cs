namespace Dhadgar.Contracts.Discord;

/// <summary>
/// Discord contracts for internal admin notifications.
/// This is for the Meridian team's Discord - not customer-facing.
/// </summary>

// ============================================================================
// Events - Published after Discord actions
// ============================================================================

/// <summary>
/// Event published when a notification is sent to Discord.
/// </summary>
public record DiscordNotificationSent(
    Guid NotificationId,
    string EventType,
    string Title,
    bool Success,
    string? ErrorMessage,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Event published when an admin command is executed via Discord.
/// </summary>
public record DiscordAdminCommandExecuted(
    string CommandName,
    ulong DiscordUserId,
    string DiscordUsername,
    bool Success,
    string? ErrorMessage,
    DateTimeOffset OccurredAtUtc);
