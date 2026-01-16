using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Services;

public interface ISecretProvider
{
    // Read operations
    Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default);
    bool IsAllowed(string secretName);

    // Write operations
    Task<bool> SetSecretAsync(string secretName, string value, CancellationToken ct = default);
    Task<(string Version, DateTime CreatedAt)> RotateSecretAsync(string secretName, CancellationToken ct = default);
    Task<bool> DeleteSecretAsync(string secretName, CancellationToken ct = default);
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

    public async Task<bool> SetSecretAsync(string secretName, string value, CancellationToken ct = default)
    {
        // Security check: only allow writing to allowed secrets
        if (!IsAllowed(secretName))
        {
            _logger.LogWarning("Attempted to write non-allowed secret: {SecretName}", secretName);
            return false;
        }

        // Validate size (25KB limit for Azure Key Vault)
        const int maxSizeBytes = 25 * 1024;
        var valueBytes = System.Text.Encoding.UTF8.GetByteCount(value);
        if (valueBytes > maxSizeBytes)
        {
            _logger.LogError("Secret {SecretName} exceeds size limit: {Size} bytes (max: {MaxSize} bytes)",
                secretName, valueBytes, maxSizeBytes);
            throw new InvalidOperationException($"Secret value exceeds {maxSizeBytes} byte limit");
        }

        try
        {
            await _client.SetSecretAsync(secretName, value, ct);

            // Invalidate cache
            var cacheKey = $"secret:{secretName}";
            _cache.Remove(cacheKey);

            _logger.LogInformation("Set secret {SecretName} in Key Vault", secretName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set secret {SecretName} in Key Vault", secretName);
            throw;
        }
    }

    public async Task<(string Version, DateTime CreatedAt)> RotateSecretAsync(string secretName, CancellationToken ct = default)
    {
        // Security check
        if (!IsAllowed(secretName))
        {
            _logger.LogWarning("Attempted to rotate non-allowed secret: {SecretName}", secretName);
            throw new UnauthorizedAccessException($"Secret '{secretName}' is not in the allowed list");
        }

        try
        {
            // Generate a new cryptographically secure random value (32 bytes = 256 bits)
            var newValue = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            // Set the new secret value (Key Vault automatically creates a new version)
            var response = await _client.SetSecretAsync(secretName, newValue, ct);

            // Invalidate cache
            var cacheKey = $"secret:{secretName}";
            _cache.Remove(cacheKey);

            _logger.LogInformation("Rotated secret {SecretName} to version {Version}", secretName, response.Value.Properties.Version);

            return (response.Value.Properties.Version ?? "unknown", response.Value.Properties.CreatedOn?.UtcDateTime ?? DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate secret {SecretName} in Key Vault", secretName);
            throw;
        }
    }

    public async Task<bool> DeleteSecretAsync(string secretName, CancellationToken ct = default)
    {
        // Security check
        if (!IsAllowed(secretName))
        {
            _logger.LogWarning("Attempted to delete non-allowed secret: {SecretName}", secretName);
            return false;
        }

        try
        {
            // Start delete operation (soft delete if enabled on vault)
            await _client.StartDeleteSecretAsync(secretName, ct);

            // Invalidate cache
            var cacheKey = $"secret:{secretName}";
            _cache.Remove(cacheKey);

            _logger.LogInformation("Deleted secret {SecretName} from Key Vault", secretName);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Attempted to delete non-existent secret: {SecretName}", secretName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret {SecretName} from Key Vault", secretName);
            throw;
        }
    }
}
