using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.OAuth;

public sealed class MockOAuthHandler : OAuthHandler<OAuthOptions>
{
    public MockOAuthHandler(
        IOptionsMonitor<OAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
    {
        var payload = System.Text.Json.JsonDocument.Parse(
            "{\"access_token\":\"mock-access\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
        return Task.FromResult(OAuthTokenResponse.Success(payload));
    }

    protected override Task<AuthenticationTicket> CreateTicketAsync(
        ClaimsIdentity identity,
        AuthenticationProperties properties,
        OAuthTokenResponse tokens)
    {
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, $"mock-{Scheme.Name}-user"));
        identity.AddClaim(new Claim(ClaimTypes.Name, $"Mock {Scheme.Name} User"));
        identity.AddClaim(new Claim(ClaimTypes.Email, $"mock-{Scheme.Name}@example.com"));

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, properties, Scheme.Name);
        return Task.FromResult(ticket);
    }
}
