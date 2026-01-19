using System.Security.Claims;
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
    private readonly ISigningKeyProvider _signingKeyProvider;

    public JwtService(
        IOptions<AuthOptions> options,
        TimeProvider timeProvider,
        ILogger<JwtService> logger,
        ISigningKeyProvider signingKeyProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _signingKeyProvider = signingKeyProvider;
    }

    public Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
        IEnumerable<Claim> claims,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresIn = _options.AccessTokenLifetimeSeconds;

        // Normalize issuer to always end with slash (must match OpenIddict server config)
        var issuer = _options.Issuer.TrimEnd('/') + "/";

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = now.AddSeconds(expiresIn).UtcDateTime,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Issuer = issuer,
            Audience = _options.Audience,
            SigningCredentials = _signingKeyProvider.GetSigningCredentials()
        };

        var accessToken = _tokenHandler.CreateToken(descriptor);
        var refreshToken = GenerateRefreshToken();

        _logger.LogDebug("Generated JWT token pair.");

        return Task.FromResult((accessToken, refreshToken, expiresIn));
    }

    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
    }
}
