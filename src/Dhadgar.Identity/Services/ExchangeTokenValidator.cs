using System.Security.Claims;
using System.Security.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Dhadgar.Identity.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dhadgar.Identity.Services;

public interface IExchangeTokenValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(string exchangeToken, CancellationToken ct = default);
}

public sealed class ExchangeTokenValidator : IExchangeTokenValidator, IDisposable
{
    private readonly ExchangeTokenOptions _options;
    private readonly AuthOptions _authOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ExchangeTokenValidator> _logger;
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private ECDsaSecurityKey? _publicKey;
    private ECDsa? _ecdsa;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public ExchangeTokenValidator(
        IOptions<ExchangeTokenOptions> options,
        IOptions<AuthOptions> authOptions,
        IHostEnvironment environment,
        ILogger<ExchangeTokenValidator> logger)
    {
        _options = options.Value;
        _authOptions = authOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string exchangeToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeToken))
        {
            _logger.LogWarning("Exchange token validation failed: token is empty");
            return null;
        }

        await EnsureInitializedAsync(ct);

        if (_publicKey is null)
        {
            _logger.LogWarning("Exchange token validation failed: public key not loaded");
            return null;
        }

        _logger.LogDebug("Validating exchange token with issuer={Issuer}, audience={Audience}",
            _options.Issuer, _options.Audience);

        var result = await _tokenHandler.ValidateTokenAsync(exchangeToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,

            ValidateAudience = true,
            ValidAudience = _options.Audience,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),

            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _publicKey
        });

        if (!result.IsValid)
        {
            _logger.LogWarning("Exchange token validation failed: {Error}", result.Exception?.Message ?? "Unknown error");
        }

        return result.IsValid ? result.ClaimsIdentity is null ? null : new ClaimsPrincipal(result.ClaimsIdentity) : null;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            (_publicKey, _ecdsa) = await LoadPublicKeyAsync(ct);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<(ECDsaSecurityKey? Key, ECDsa? Ecdsa)> LoadPublicKeyAsync(CancellationToken ct)
    {
        // 1. Try Key Vault first (preferred)
        var vaultUri = _authOptions.KeyVault.VaultUri;
        var secretName = _authOptions.KeyVault.ExchangePublicKeyName;

        if (!string.IsNullOrWhiteSpace(vaultUri) && !string.IsNullOrWhiteSpace(secretName))
        {
            try
            {
                var client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
                var response = await client.GetSecretAsync(secretName, cancellationToken: ct);
                var publicKeyPem = response.Value.Value;

                if (!string.IsNullOrWhiteSpace(publicKeyPem))
                {
                    var ecdsa = ECDsa.Create();
                    ecdsa.ImportFromPem(publicKeyPem);
                    return (new ECDsaSecurityKey(ecdsa), ecdsa);
                }
            }
            catch (Exception ex)
            {
                if (_environment.IsProduction())
                {
                    throw new InvalidOperationException(
                        $"Failed to load exchange token public key from Key Vault: {ex.Message}", ex);
                }
            }
        }

        // 2. Try inline PEM from config
        var publicKeyPemConfig = _options.PublicKeyPem;
        if (!string.IsNullOrWhiteSpace(publicKeyPemConfig))
        {
            _logger.LogInformation("Loading exchange token public key from configuration");
            // Handle escaped newlines from environment variables (e.g., "-----BEGIN...-----\n...\n-----END...-----")
            var normalizedPem = publicKeyPemConfig.Replace("\\n", "\n", StringComparison.Ordinal);
            _logger.LogDebug("Public key PEM (first 50 chars): {PemStart}...", normalizedPem[..Math.Min(50, normalizedPem.Length)]);
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(normalizedPem);
            _logger.LogInformation("Exchange token public key loaded successfully from configuration");
            return (new ECDsaSecurityKey(ecdsa), ecdsa);
        }

        // 3. Try file path
        if (!string.IsNullOrWhiteSpace(_options.PublicKeyPath) && File.Exists(_options.PublicKeyPath))
        {
            var publicKeyPemFile = await File.ReadAllTextAsync(_options.PublicKeyPath, ct);
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPemFile);
            return (new ECDsaSecurityKey(ecdsa), ecdsa);
        }

        // 4. Production requires a key
        if (_environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Exchange token public key is required in production. " +
                "Configure Auth:KeyVault:VaultUri and Auth:KeyVault:ExchangePublicKeyName.");
        }

        // 5. Dev mode: generate ephemeral key (won't validate real tokens but allows startup)
        _logger.LogWarning("No exchange token public key configured - using ephemeral key (token validation will fail)");
        var ephemeral = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (new ECDsaSecurityKey(ephemeral), ephemeral);
    }

    public void Dispose()
    {
        _ecdsa?.Dispose();
        _initLock.Dispose();
    }
}
