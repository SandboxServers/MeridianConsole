using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Services;

public sealed class DevelopmentSecretProvider : ISecretProvider
{
    private readonly Dictionary<string, string> _secrets;
    private readonly HashSet<string> _allowedSecrets;
    private readonly ILogger<DevelopmentSecretProvider> _logger;

    public DevelopmentSecretProvider(
        IConfiguration configuration,
        IOptions<SecretsOptions> options,
        ILogger<DevelopmentSecretProvider> logger)
    {
        _logger = logger;
        var secretSection = configuration.GetSection("Secrets:Development:Secrets");
        _secrets = secretSection.GetChildren()
            .Select(child => (Key: child.Key, Value: child.Value))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .Where(entry => !string.Equals(entry.Value, "PLACEHOLDER-UPDATE-ME", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.OrdinalIgnoreCase);

        _allowedSecrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _allowedSecrets.UnionWith(options.Value.AllowedSecrets.OAuth);
        _allowedSecrets.UnionWith(options.Value.AllowedSecrets.BetterAuth);
        _allowedSecrets.UnionWith(options.Value.AllowedSecrets.Infrastructure);
    }

    public bool IsAllowed(string secretName)
    {
        return _allowedSecrets.Contains(secretName);
    }

    public Task<string?> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        if (!IsAllowed(secretName))
        {
            _logger.LogDebug("Secret {SecretName} is not in the allowed list.", secretName);
            return Task.FromResult<string?>(null);
        }

        if (_secrets.TryGetValue(secretName, out var value))
        {
            _logger.LogDebug("Retrieved secret {SecretName} from development provider.", secretName);
            return Task.FromResult<string?>(value);
        }

        _logger.LogWarning("Development secret {SecretName} was not found.", secretName);
        return Task.FromResult<string?>(null);
    }

    public Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretNames);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var secretName in secretNames.Where(IsAllowed))
        {
            if (_secrets.TryGetValue(secretName, out var value))
            {
                result[secretName] = value;
                _logger.LogDebug("Retrieved secret {SecretName} from development provider.", secretName);
            }
            else
            {
                _logger.LogWarning("Development secret {SecretName} was not found.", secretName);
            }
        }

        return Task.FromResult(result);
    }
}
