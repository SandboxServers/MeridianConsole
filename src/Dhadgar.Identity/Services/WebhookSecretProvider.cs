using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Dhadgar.Identity.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.Services;

public interface IWebhookSecretProvider
{
    /// <summary>
    /// Retrieves the Better Auth webhook secret from Key Vault.
    /// Returns null if Key Vault is not configured (Development mode).
    /// </summary>
    Task<string?> GetBetterAuthSecretAsync(CancellationToken ct = default);
}

public sealed class WebhookSecretProvider : IWebhookSecretProvider
{
    private readonly IMemoryCache _cache;
    private readonly AuthOptions _authOptions;
    private readonly WebhookOptions _webhookOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WebhookSecretProvider> _logger;

    private const string CacheKey = "webhook:betterauth:secret";

    public WebhookSecretProvider(
        IMemoryCache cache,
        IOptions<AuthOptions> authOptions,
        IOptions<WebhookOptions> webhookOptions,
        IHostEnvironment environment,
        ILogger<WebhookSecretProvider> logger)
    {
        _cache = cache;
        _authOptions = authOptions.Value;
        _webhookOptions = webhookOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<string?> GetBetterAuthSecretAsync(CancellationToken ct = default)
    {
        // Try to get from cache first
        if (_cache.TryGetValue(CacheKey, out string? cachedSecret))
        {
            return cachedSecret;
        }

        // Check if Key Vault is configured
        var vaultUri = _authOptions.KeyVault?.VaultUri;
        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    "Key Vault not configured for webhook secrets. " +
                    "Webhook signature validation will be skipped in Development mode.");
                return null;
            }

            _logger.LogError(
                "Key Vault not configured for webhook secrets. " +
                "Configure Auth:KeyVault:VaultUri in production.");
            throw new InvalidOperationException("Key Vault configuration is required for webhook secrets in production.");
        }

        var secretName = _webhookOptions.BetterAuthSecretName;
        if (string.IsNullOrWhiteSpace(secretName))
        {
            _logger.LogError("Webhook secret name not configured (Webhooks:BetterAuthSecretName)");
            throw new InvalidOperationException("Webhook secret name is required.");
        }

        try
        {
            _logger.LogDebug("Fetching webhook secret '{SecretName}' from Key Vault", secretName);

            var credential = new DefaultAzureCredential();
            var client = new SecretClient(new Uri(vaultUri), credential);

            var response = await client.GetSecretAsync(secretName, cancellationToken: ct);
            var secret = response.Value.Value;

            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogError("Webhook secret '{SecretName}' exists but is empty", secretName);
                throw new InvalidOperationException($"Webhook secret '{secretName}' is empty.");
            }

            // Cache the secret
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(_webhookOptions.SecretCacheMinutes))
                .SetPriority(CacheItemPriority.High);

            _cache.Set(CacheKey, secret, cacheOptions);

            _logger.LogInformation(
                "Webhook secret '{SecretName}' loaded from Key Vault and cached for {Minutes} minutes",
                secretName,
                _webhookOptions.SecretCacheMinutes);

            return secret;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogError(
                "Webhook secret '{SecretName}' not found in Key Vault at {VaultUri}",
                secretName,
                vaultUri);
            throw new InvalidOperationException($"Webhook secret '{secretName}' not found in Key Vault.", ex);
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Failed to retrieve webhook secret '{SecretName}' from Key Vault: {Status} {Message}",
                secretName,
                ex.Status,
                ex.Message);
            throw;
        }
    }
}
