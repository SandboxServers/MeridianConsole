using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.OAuth;

public sealed class EpicGamesOAuthOptions : OAuthOptions
{
    public EpicGamesOAuthOptions()
    {
        ClaimsIssuer = "EpicGames";
        Scope.Add("basic_profile");
    }
}

public sealed class EpicGamesOAuthHandler : OAuthHandler<EpicGamesOAuthOptions>
{
    public EpicGamesOAuthHandler(
        IOptionsMonitor<EpicGamesOAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticationTicket> CreateTicketAsync(
        ClaimsIdentity identity,
        AuthenticationProperties properties,
        OAuthTokenResponse tokens)
    {
        if (string.IsNullOrWhiteSpace(Options.UserInformationEndpoint))
        {
            throw new InvalidOperationException("Epic Games user info endpoint is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await Backchannel.SendAsync(request, Context.RequestAborted);
        response.EnsureSuccessStatusCode();

        await using var payload = await response.Content.ReadAsStreamAsync(Context.RequestAborted);
        using var document = await System.Text.Json.JsonDocument.ParseAsync(payload, cancellationToken: Context.RequestAborted);
        var root = document.RootElement;

        var accountId = ReadString(root, "account_id", "accountId", "id", "sub");
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, accountId));
        }

        var email = ReadString(root, "email", "emailAddress");
        if (!string.IsNullOrWhiteSpace(email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
        }

        var displayName = ReadString(root, "displayName", "display_name", "name");
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), properties, Scheme.Name);
        return ticket;
    }

    private static string? ReadString(System.Text.Json.JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}

public static class EpicGamesAuthenticationExtensions
{
    public static AuthenticationBuilder AddEpicGames(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        Action<EpicGamesOAuthOptions> configureOptions)
    {
        return builder.AddEpicGames(authenticationScheme, displayName: "Epic Games", configureOptions);
    }

    public static AuthenticationBuilder AddEpicGames(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        string displayName,
        Action<EpicGamesOAuthOptions> configureOptions)
    {
        return builder.AddOAuth<EpicGamesOAuthOptions, EpicGamesOAuthHandler>(
            authenticationScheme,
            displayName,
            configureOptions);
    }
}
