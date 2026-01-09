using Dhadgar.Secrets.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Dhadgar.Secrets.Services;

public sealed class DevelopmentSecretProvider : ISecretProvider
{
    private readonly Dictionary<string, string> _secrets;
    private readonly HashSet<string> _allowedSecrets;

    public DevelopmentSecretProvider(
        IConfiguration configuration,
        IOptions<SecretsOptions> options)
    {
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
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(_secrets.TryGetValue(secretName, out var value) ? value : null);
    }

    public Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> secretNames, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var secretName in secretNames.Where(IsAllowed))
        {
            if (_secrets.TryGetValue(secretName, out var value))
            {
                result[secretName] = value;
            }
        }

        return Task.FromResult(result);
    }
}
