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
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private ECDsaSecurityKey? _publicKey;
    private ECDsa? _ecdsa;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public ExchangeTokenValidator(
        IOptions<ExchangeTokenOptions> options,
        IOptions<AuthOptions> authOptions,
        IHostEnvironment environment)
    {
        _options = options.Value;
        _authOptions = authOptions.Value;
        _environment = environment;
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string exchangeToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeToken))
        {
            return null;
        }

        await EnsureInitializedAsync(ct);

        if (_publicKey is null)
        {
            return null;
        }

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
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPemConfig);
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
        var ephemeral = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (new ECDsaSecurityKey(ephemeral), ephemeral);
    }

    public void Dispose()
    {
        _ecdsa?.Dispose();
        _initLock.Dispose();
    }
}
