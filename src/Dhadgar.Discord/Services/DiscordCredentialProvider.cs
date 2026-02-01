using System.Net.Http.Json;

namespace Dhadgar.Discord.Services;

/// <summary>
/// Provides Discord credentials from configuration or the Secrets service.
/// For local dev, use user-secrets: dotnet user-secrets set "Discord:BotToken" "your-token"
/// In production, credentials are retrieved from the Secrets service.
/// </summary>
public sealed class DiscordCredentialProvider : IDiscordCredentialProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DiscordCredentialProvider> _logger;

    // Cache credentials with TTL (30 minutes for secrets, longer for config)
    private static readonly TimeSpan SecretsCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ConfigCacheDuration = TimeSpan.FromHours(24);

    private CachedCredential? _botToken;
    private CachedCredential? _clientId;
    private CachedCredential? _clientSecret;

    public DiscordCredentialProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DiscordCredentialProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GetBotTokenAsync(CancellationToken ct = default)
    {
        if (_botToken is not null && !_botToken.IsExpired)
            return _botToken.Value;

        // Try config first (user-secrets for local dev)
        var configToken = _configuration["Discord:BotToken"];
        if (!string.IsNullOrWhiteSpace(configToken))
        {
            _logger.LogDebug("Using Discord bot token from configuration");
            _botToken = new CachedCredential(configToken, ConfigCacheDuration);
            return _botToken.Value;
        }

        // Fall back to Secrets service
        var token = await GetSecretAsync("discord-bot-token", ct);
        _botToken = new CachedCredential(token, SecretsCacheDuration);
        return _botToken.Value;
    }

    public async Task<string> GetClientIdAsync(CancellationToken ct = default)
    {
        if (_clientId is not null && !_clientId.IsExpired)
            return _clientId.Value;

        var configId = _configuration["Discord:ClientId"];
        if (!string.IsNullOrWhiteSpace(configId))
        {
            _logger.LogDebug("Using Discord client ID from configuration");
            _clientId = new CachedCredential(configId, ConfigCacheDuration);
            return _clientId.Value;
        }

        var id = await GetSecretAsync("oauth-discord-client-id", ct);
        _clientId = new CachedCredential(id, SecretsCacheDuration);
        return _clientId.Value;
    }

    public async Task<string> GetClientSecretAsync(CancellationToken ct = default)
    {
        if (_clientSecret is not null && !_clientSecret.IsExpired)
            return _clientSecret.Value;

        var configSecret = _configuration["Discord:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(configSecret))
        {
            _logger.LogDebug("Using Discord client secret from configuration");
            _clientSecret = new CachedCredential(configSecret, ConfigCacheDuration);
            return _clientSecret.Value;
        }

        var secret = await GetSecretAsync("oauth-discord-client-secret", ct);
        _clientSecret = new CachedCredential(secret, SecretsCacheDuration);
        return _clientSecret.Value;
    }

    private async Task<string> GetSecretAsync(string secretName, CancellationToken ct)
    {
        _logger.LogDebug("Fetching secret {SecretName} from Secrets service", secretName);

        try
        {
            var response = await _httpClient.GetAsync($"api/v1/secrets/{secretName}", ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SecretResponse>(ct);
            if (result?.Value is null)
            {
                throw new InvalidOperationException($"Secret '{secretName}' not found or has no value");
            }

            _logger.LogDebug("Successfully retrieved secret {SecretName}", secretName);
            return result.Value;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret {SecretName} from Secrets service", secretName);
            throw new InvalidOperationException($"Failed to retrieve secret '{secretName}' from Secrets service", ex);
        }
    }

    private sealed class CachedCredential
    {
        public string Value { get; }
        public DateTimeOffset ExpiresAt { get; }
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

        public CachedCredential(string value, TimeSpan ttl)
        {
            Value = value;
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl);
        }
    }

#pragma warning disable CA1812 // Internal class is instantiated via JSON deserialization
    private sealed record SecretResponse(string Name, string Value);
#pragma warning restore CA1812
}
