using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Services;

public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default);
    bool IsAllowed(string secretName);
}

public sealed class KeyVaultSecretProvider : ISecretProvider
{
    private readonly SecretClient _client;
    private readonly SecretsOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<KeyVaultSecretProvider> _logger;
    private readonly HashSet<string> _allowedSecrets;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public KeyVaultSecretProvider(
        IOptions<SecretsOptions> options,
        IMemoryCache cache,
        ILogger<KeyVaultSecretProvider> logger)
    {
        _options = options.Value;
        _cache = cache;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.KeyVaultUri))
        {
            throw new InvalidOperationException("KeyVaultUri is required for Secrets service.");
        }

        _client = new SecretClient(new Uri(_options.KeyVaultUri), new DefaultAzureCredential());

        // Build the allowed secrets set from configuration
        _allowedSecrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _allowedSecrets.UnionWith(_options.AllowedSecrets.OAuth);
        _allowedSecrets.UnionWith(_options.AllowedSecrets.BetterAuth);
        _allowedSecrets.UnionWith(_options.AllowedSecrets.Infrastructure);
    }

    public bool IsAllowed(string secretName)
    {
        return _allowedSecrets.Contains(secretName);
    }

    public async Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        // Security check: only dispense allowed secrets
        if (!IsAllowed(secretName))
        {
            _logger.LogWarning("Attempted to access non-allowed secret: {SecretName}", secretName);
            return null;
        }

        // Check cache first
        var cacheKey = $"secret:{secretName}";
        if (_cache.TryGetValue(cacheKey, out string? cachedValue))
        {
            return cachedValue;
        }

        try
        {
            var response = await _client.GetSecretAsync(secretName, cancellationToken: ct);
            var value = response.Value.Value;

            // Skip placeholder values
            if (value == "PLACEHOLDER-UPDATE-ME")
            {
                _logger.LogDebug("Secret {SecretName} is a placeholder, returning null", secretName);
                return null;
            }

            // Cache the value
            _cache.Set(cacheKey, value, _cacheDuration);

            _logger.LogDebug("Retrieved secret {SecretName} from Key Vault", secretName);
            return value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Secret {SecretName} not found in Key Vault", secretName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret {SecretName} from Key Vault", secretName);
            throw;
        }
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretNames);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var tasks = secretNames
            .Where(IsAllowed)
            .Select(async name =>
            {
                var value = await GetSecretAsync(name, ct);
                return (name, value);
            });

        var results = await Task.WhenAll(tasks);

        foreach (var (name, value) in results)
        {
            if (!string.IsNullOrEmpty(value))
            {
                result[name] = value;
            }
        }

        return result;
    }
}
