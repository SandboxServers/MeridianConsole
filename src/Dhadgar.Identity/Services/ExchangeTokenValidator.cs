using System.Security.Claims;
using System.Security.Cryptography;
using Dhadgar.Identity.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dhadgar.Identity.Services;

public interface IExchangeTokenValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(string exchangeToken, CancellationToken ct = default);
}

public sealed class ExchangeTokenValidator : IExchangeTokenValidator
{
    private readonly ExchangeTokenOptions _options;
    private readonly ECDsaSecurityKey _publicKey;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public ExchangeTokenValidator(IOptions<ExchangeTokenOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;
        _publicKey = LoadPublicKey(_options, environment);
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string exchangeToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeToken))
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

    private static ECDsaSecurityKey LoadPublicKey(ExchangeTokenOptions options, IHostEnvironment environment)
    {
        var publicKeyPem = options.PublicKeyPem;

        if (string.IsNullOrWhiteSpace(publicKeyPem) && !string.IsNullOrWhiteSpace(options.PublicKeyPath))
        {
            if (File.Exists(options.PublicKeyPath))
            {
                publicKeyPem = File.ReadAllText(options.PublicKeyPath);
            }
        }

        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException("Exchange token public key is required in production.");
            }

            var ephemeral = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return new ECDsaSecurityKey(ephemeral);
        }

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(publicKeyPem);
        return new ECDsaSecurityKey(ecdsa);
    }
}
