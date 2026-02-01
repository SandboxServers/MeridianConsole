using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Dhadgar.Identity.Options;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Service for generating client assertion JWTs for federated identity scenarios.
/// These assertions allow services to authenticate to external providers (like Microsoft)
/// using tokens signed by our Identity provider instead of static client secrets.
/// </summary>
public interface IClientAssertionService
{
    /// <summary>
    /// Generates a client assertion JWT for Microsoft Entra ID federated credential authentication.
    /// </summary>
    /// <param name="subject">The subject claim (must match the federated credential's subject)</param>
    /// <param name="audience">The audience claim (typically "api://AzureADTokenExchange")</param>
    /// <returns>A signed JWT suitable for use as client_assertion</returns>
    string GenerateMicrosoftAssertion(string subject, string audience = "api://AzureADTokenExchange");
}

public sealed class ClientAssertionService : IClientAssertionService
{
    private readonly AuthOptions _options;
    private readonly ISigningKeyProvider _signingKeyProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientAssertionService> _logger;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public ClientAssertionService(
        IOptions<AuthOptions> options,
        ISigningKeyProvider signingKeyProvider,
        TimeProvider timeProvider,
        ILogger<ClientAssertionService> logger)
    {
        _options = options.Value;
        _signingKeyProvider = signingKeyProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string GenerateMicrosoftAssertion(string subject, string audience = "api://AzureADTokenExchange")
    {
        var now = _timeProvider.GetUtcNow();

        // Issuer must match exactly what's configured in the federated credential (WITH trailing slash)
        var issuer = _options.Issuer.TrimEnd('/') + "/";

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", subject),
                new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString())
            }),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(10).UtcDateTime, // Short-lived assertion
            SigningCredentials = _signingKeyProvider.GetSigningCredentials()
        };

        var token = _tokenHandler.CreateToken(descriptor);

        _logger.LogDebug(
            "Generated client assertion for subject={Subject}, audience={Audience}, issuer={Issuer}",
            subject, audience, issuer);

        return token;
    }
}
