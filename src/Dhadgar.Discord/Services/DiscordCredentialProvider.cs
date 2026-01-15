using System.Net.Http.Json;

namespace Dhadgar.Discord.Services;

/// <summary>
/// Provides Discord credentials from the Secrets service.
/// </summary>
public sealed class DiscordCredentialProvider : IDiscordCredentialProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DiscordCredentialProvider> _logger;

    // Cache credentials in memory after first fetch
    private string? _botToken;
    private string? _clientId;
    private string? _clientSecret;

    public DiscordCredentialProvider(
        HttpClient httpClient,
        ILogger<DiscordCredentialProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> GetBotTokenAsync(CancellationToken ct = default)
    {
        if (_botToken is not null)
            return _botToken;

        _botToken = await GetSecretAsync("discord-bot-token", ct);
        return _botToken;
    }

    public async Task<string> GetClientIdAsync(CancellationToken ct = default)
    {
        if (_clientId is not null)
            return _clientId;

        _clientId = await GetSecretAsync("oauth-discord-client-id", ct);
        return _clientId;
    }

    public async Task<string> GetClientSecretAsync(CancellationToken ct = default)
    {
        if (_clientSecret is not null)
            return _clientSecret;

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

    private sealed record SecretResponse(string Name, string Value);
}
