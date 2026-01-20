using Refit;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Refit interface for Discord service admin API.
/// </summary>
public interface IDiscordApi
{
    [Get("/api/v1/discord/logs")]
    Task<IReadOnlyList<DiscordLogDto>> GetLogsAsync(
        [Header("X-Admin-Api-Key")] string apiKey,
        [Query] int? limit = null,
        [Query] Guid? orgId = null,
        [Header("X-Tenant-Id")] string? tenantId = null,
        CancellationToken ct = default);

    [Get("/healthz")]
    Task<DiscordHealthDto> GetHealthAsync(CancellationToken ct = default);

    [Get("/api/v1/platform/health")]
    Task<PlatformHealthDto> GetPlatformHealthAsync(
        [Header("X-Admin-Api-Key")] string apiKey,
        CancellationToken ct = default);

    [Get("/api/v1/discord/channels")]
    Task<DiscordChannelsDto> GetChannelsAsync(
        [Header("X-Admin-Api-Key")] string apiKey,
        [Query] ulong? guildId = null,
        CancellationToken ct = default);
}

/// <summary>
/// DTO for Discord notification log entries.
/// </summary>
public record DiscordLogDto(
    Guid id,
    Guid organizationId,
    string eventType,
    string channel,
    string title,
    string status,
    string? errorMessage,
    DateTimeOffset createdAtUtc);

/// <summary>
/// DTO for Discord service health check.
/// </summary>
public record DiscordHealthDto(
    string Service,
    string Status,
    string BotStatus);

/// <summary>
/// DTO for platform-wide health check.
/// </summary>
public record PlatformHealthDto(
    IReadOnlyList<ServiceHealthDto> Services,
    int HealthyCount,
    int UnhealthyCount,
    DateTimeOffset CheckedAtUtc);

/// <summary>
/// DTO for individual service health.
/// </summary>
public record ServiceHealthDto(
    string Name,
    string Url,
    bool IsHealthy,
    long? ResponseTimeMs,
    string? Error);

/// <summary>
/// DTO for Discord channels response.
/// </summary>
public record DiscordChannelsDto(
    bool Connected,
    int GuildCount,
    IReadOnlyList<DiscordGuildDto> Guilds,
    string? Message = null);

/// <summary>
/// DTO for a Discord guild (server).
/// </summary>
public record DiscordGuildDto(
    ulong GuildId,
    string GuildName,
    IReadOnlyList<DiscordChannelDto> Channels);

/// <summary>
/// DTO for a Discord text channel.
/// </summary>
public record DiscordChannelDto(
    ulong ChannelId,
    string Name,
    string? Category,
    int Position);
