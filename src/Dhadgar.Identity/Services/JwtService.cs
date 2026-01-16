using System.Security.Claims;
using System.Security.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Dhadgar.Identity.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dhadgar.Identity.Services;

public interface IJwtService
{
    Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
        IEnumerable<Claim> claims,
        CancellationToken ct = default);
}

public sealed class JwtService : IJwtService, IDisposable
{
    private readonly AuthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JwtService> _logger;
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private readonly SigningCredentials _signingCredentials;
    private readonly ECDsa? _ecdsa;

    public JwtService(IOptions<AuthOptions> options, TimeProvider timeProvider, ILogger<JwtService> logger, IHostEnvironment environment)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        (_signingCredentials, _ecdsa) = LoadSigningCredentials(_options, environment, _logger);
    }

    public Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
        IEnumerable<Claim> claims,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresIn = _options.AccessTokenLifetimeSeconds;

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = now.AddSeconds(expiresIn).UtcDateTime,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = _signingCredentials
        };

        var accessToken = _tokenHandler.CreateToken(descriptor);
        var refreshToken = GenerateRefreshToken();

        _logger.LogDebug("Generated JWT token pair.");

        return Task.FromResult((accessToken, refreshToken, expiresIn));
    }

    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    private static (SigningCredentials Credentials, ECDsa? Key) LoadSigningCredentials(AuthOptions options, IHostEnvironment environment, ILogger logger)
    {
        // Prefer Key Vault key if configured
        if (!string.IsNullOrWhiteSpace(options.KeyVault?.VaultUri) &&
            !string.IsNullOrWhiteSpace(options.KeyVault.JwtSigningKeyName))
        {
            var credential = new DefaultAzureCredential();
            var secretClient = new SecretClient(new Uri(options.KeyVault!.VaultUri), credential);

            try
            {
                var secret = secretClient.GetSecret(options.KeyVault.JwtSigningKeyName);
                var pem = secret.Value.Value;
                var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pem);
                var credentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa)
                {
                    KeyId = string.IsNullOrWhiteSpace(options.SigningKeyKid) ? null : options.SigningKeyKid
                }, SecurityAlgorithms.EcdsaSha256);
                return (credentials, ecdsa);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Key Vault signing key is required in production.", ex);
            }
        }

        var signingKey = LoadLocalEcdsa(options, environment);
        var signingCredentials = new SigningCredentials(new ECDsaSecurityKey(signingKey)
        {
            KeyId = string.IsNullOrWhiteSpace(options.SigningKeyKid) ? null : options.SigningKeyKid
        }, SecurityAlgorithms.EcdsaSha256);

        return (signingCredentials, signingKey);
    }

    private static ECDsa LoadLocalEcdsa(AuthOptions options, IHostEnvironment environment)
    {
        var pem = options.SigningKeyPem;

        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(options.SigningKeyPath))
        {
            if (File.Exists(options.SigningKeyPath))
            {
                pem = File.ReadAllText(options.SigningKeyPath);
            }
        }

        if (string.IsNullOrWhiteSpace(pem))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException("JWT signing key is required in production.");
            }

            return ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return ecdsa;
    }

    public void Dispose()
    {
        _ecdsa?.Dispose();
    }
}
