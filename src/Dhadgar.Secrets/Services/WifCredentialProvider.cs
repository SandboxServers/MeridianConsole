using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Services;

/// <summary>
/// Provides Azure credentials via Workload Identity Federation (WIF).
/// Gets a token from the Identity service and uses it to authenticate to Azure AD.
/// </summary>
public interface IWifCredentialProvider
{
    /// <summary>
    /// Gets an Azure TokenCredential for accessing Azure resources.
    /// Uses WIF if configured, otherwise falls back to DefaultAzureCredential.
    /// </summary>
    TokenCredential GetCredential();
}

public sealed class WifCredentialProvider : IWifCredentialProvider
{
    private readonly SecretsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WifCredentialProvider> _logger;
    private readonly TokenCredential _credential;

    public WifCredentialProvider(
        IOptions<SecretsOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<WifCredentialProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _credential = CreateCredential();
    }

    public TokenCredential GetCredential() => _credential;

    private TokenCredential CreateCredential()
    {
        var wifConfig = _options.Wif;

        // If WIF is not configured, fall back to DefaultAzureCredential
        if (wifConfig is null)
        {
            _logger.LogWarning("WIF configuration section is null, falling back to DefaultAzureCredential");
            return new DefaultAzureCredential();
        }

        if (string.IsNullOrWhiteSpace(wifConfig.TenantId))
        {
            _logger.LogWarning("WIF TenantId is not configured, falling back to DefaultAzureCredential");
            return new DefaultAzureCredential();
        }

        if (string.IsNullOrWhiteSpace(wifConfig.ClientId))
        {
            _logger.LogWarning("WIF ClientId is not configured, falling back to DefaultAzureCredential");
            return new DefaultAzureCredential();
        }

        if (string.IsNullOrWhiteSpace(wifConfig.IdentityTokenEndpoint))
        {
            _logger.LogWarning("WIF IdentityTokenEndpoint is not configured, falling back to DefaultAzureCredential");
            return new DefaultAzureCredential();
        }

        _logger.LogInformation(
            "WIF credential configured successfully: TenantId={TenantId}, ClientId={ClientId}, TokenEndpoint={TokenEndpoint}, ServiceClientId={ServiceClientId}",
            wifConfig.TenantId,
            wifConfig.ClientId,
            wifConfig.IdentityTokenEndpoint,
            wifConfig.ServiceClientId ?? "(default)");

        // Create a ClientAssertionCredential that gets tokens from our Identity service
        return new ClientAssertionCredential(
            wifConfig.TenantId,
            wifConfig.ClientId,
            async (ct) => await GetIdentityTokenAsync(ct));
    }

    private async Task<string> GetIdentityTokenAsync(CancellationToken ct)
    {
        var wifConfig = _options.Wif!;
        var httpClient = _httpClientFactory.CreateClient("IdentityWif");
        var serviceClientId = wifConfig.ServiceClientId ?? "dev-client";

        _logger.LogInformation(
            "Requesting WIF token from Identity service: Endpoint={Endpoint}, ServiceClientId={ServiceClientId}",
            wifConfig.IdentityTokenEndpoint,
            serviceClientId);

        // Request a WIF-compatible token from the Identity service
        using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = serviceClientId,
            ["client_secret"] = wifConfig.ServiceClientSecret ?? "dev-secret",
            ["scope"] = "wif"
        });

        var response = await httpClient.PostAsync(wifConfig.IdentityTokenEndpoint, tokenRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Failed to get WIF token from Identity service: StatusCode={StatusCode}, Endpoint={Endpoint}, ServiceClientId={ServiceClientId}, Error={Error}",
                response.StatusCode,
                wifConfig.IdentityTokenEndpoint,
                serviceClientId,
                error);
            throw new InvalidOperationException($"Failed to get WIF token: {response.StatusCode} - {error}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new InvalidOperationException("Invalid token response from Identity service");

        _logger.LogInformation(
            "Successfully obtained WIF token from Identity service (expires in {ExpiresIn}s)",
            tokenResponse.ExpiresIn);

        return tokenResponse.AccessToken;
    }

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
