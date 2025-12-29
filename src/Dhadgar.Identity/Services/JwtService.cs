using System.Security.Claims;
using System.Security.Cryptography;
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

public sealed class JwtService : IJwtService
{
    private readonly AuthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JwtService> _logger;
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private readonly ECDsa _signingKey;
    private readonly SigningCredentials _signingCredentials;

    public JwtService(IOptions<AuthOptions> options, TimeProvider timeProvider, ILogger<JwtService> logger, IHostEnvironment environment)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _signingKey = LoadSigningKey(_options, environment);
        _signingCredentials = new SigningCredentials(new ECDsaSecurityKey(_signingKey)
        {
            KeyId = string.IsNullOrWhiteSpace(_options.SigningKeyKid) ? null : _options.SigningKeyKid
        }, SecurityAlgorithms.EcdsaSha256);
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

    private static ECDsa LoadSigningKey(AuthOptions options, IHostEnvironment environment)
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
}
