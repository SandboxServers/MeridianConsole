using System.Net.Http.Json;

namespace Dhadgar.Identity.OAuth;

/// <summary>
/// Loads gaming OAuth provider secrets from the Secrets Service.
/// The Secrets Service is the "dispersing officer" that controls access to Key Vault.
/// Identity service has direct Key Vault access for core secrets (signing certs, JWT keys),
/// but gaming OAuth secrets are retrieved via the Secrets Service.
/// </summary>
public sealed class OAuthSecretProvider
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public OAuthSecretProvider(string secretsServiceUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(secretsServiceUrl.TrimEnd('/') + "/")
        };
    }

    /// <summary>
    /// Loads all gaming OAuth provider secrets from Secrets Service.
    /// </summary>
    public async Task LoadSecretsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/v1/secrets/oauth", ct);

            if (!response.IsSuccessStatusCode)
            {
                // Secrets service unavailable - secrets will be empty, fallback to config
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<SecretsResponse>(ct);

            if (result?.Secrets != null)
            {
                foreach (var (key, value) in result.Secrets)
                {
                    _secrets[key] = value;
                }
            }
        }
        catch (HttpRequestException)
        {
            // Secrets service unavailable - secrets will be empty, fallback to config
        }
    }

    /// <summary>
    /// Gets a secret value, or null if not found.
    /// </summary>
    public string? GetSecret(string secretName)
    {
        return _secrets.TryGetValue(secretName, out var value) ? value : null;
    }

    /// <summary>
    /// Gets the Steam API key from Secrets Service.
    /// </summary>
    public string? SteamApiKey => GetSecret("oauth-steam-api-key");

    /// <summary>
    /// Gets the Battle.net client ID from Secrets Service.
    /// </summary>
    public string? BattleNetClientId => GetSecret("oauth-battlenet-client-id");

    /// <summary>
    /// Gets the Battle.net client secret from Secrets Service.
    /// </summary>
    public string? BattleNetClientSecret => GetSecret("oauth-battlenet-client-secret");

    /// <summary>
    /// Gets the Epic Games client ID from Secrets Service.
    /// </summary>
    public string? EpicClientId => GetSecret("oauth-epic-client-id");

    /// <summary>
    /// Gets the Epic Games client secret from Secrets Service.
    /// </summary>
    public string? EpicClientSecret => GetSecret("oauth-epic-client-secret");

    /// <summary>
    /// Gets the Xbox (Microsoft) client ID from Secrets Service.
    /// </summary>
    public string? XboxClientId => GetSecret("oauth-xbox-client-id");

    /// <summary>
    /// Gets the Xbox (Microsoft) client secret from Secrets Service.
    /// </summary>
    public string? XboxClientSecret => GetSecret("oauth-xbox-client-secret");

    private record SecretsResponse(Dictionary<string, string> Secrets);
}
