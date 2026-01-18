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

    // Cache credentials in memory after first fetch
    private string? _botToken;
    private string? _clientId;
    private string? _clientSecret;

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
        if (_botToken is not null)
            return _botToken;

        // Try config first (user-secrets for local dev)
        var configToken = _configuration["Discord:BotToken"];
        if (!string.IsNullOrEmpty(configToken))
        {
            _logger.LogDebug("Using Discord bot token from configuration");
            _botToken = configToken;
            return _botToken;
        }

        // Fall back to Secrets service
        _botToken = await GetSecretAsync("discord-bot-token", ct);
        return _botToken;
    }

    public async Task<string> GetClientIdAsync(CancellationToken ct = default)
    {
        if (_clientId is not null)
            return _clientId;

        var configId = _configuration["Discord:ClientId"];
        if (!string.IsNullOrEmpty(configId))
        {
            _logger.LogDebug("Using Discord client ID from configuration");
            _clientId = configId;
            return _clientId;
        }

        _clientId = await GetSecretAsync("oauth-discord-client-id", ct);
        return _clientId;
    }

    public async Task<string> GetClientSecretAsync(CancellationToken ct = default)
    {
        if (_clientSecret is not null)
            return _clientSecret;

        var configSecret = _configuration["Discord:ClientSecret"];
        if (!string.IsNullOrEmpty(configSecret))
        {
            _logger.LogDebug("Using Discord client secret from configuration");
            _clientSecret = configSecret;
            return _clientSecret;
        }

        _clientSecret = await GetSecretAsync("oauth-discord-client-secret", ct);
        return _clientSecret;
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

#pragma warning disable CA1812 // Internal class is instantiated via JSON deserialization
    private sealed record SecretResponse(string Name, string Value);
#pragma warning restore CA1812
}
