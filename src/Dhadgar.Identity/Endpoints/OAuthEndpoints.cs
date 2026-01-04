using Dhadgar.Identity.OAuth;
using Microsoft.AspNetCore.Authentication;

namespace Dhadgar.Identity.Endpoints;

public static class OAuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/oauth/{provider}/link", BeginLink)
            .RequireRateLimiting("auth");
    }

    private static IResult BeginLink(HttpContext context, string provider, string? returnUrl, IConfiguration configuration)
    {
        if (!OAuthProviderRegistry.IsSupported(provider))
        {
            return Results.NotFound();
        }

        if (!TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var redirectTarget = "/";
        if (!string.IsNullOrWhiteSpace(returnUrl) && IsAllowedRedirect(returnUrl, configuration))
        {
            redirectTarget = returnUrl;
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectTarget
        };

        properties.Items[OAuthLinkingHandler.LinkUserIdItem] = userId.ToString("D");

        return Results.Challenge(properties, new[] { provider });
    }

    private static bool TryGetUserId(HttpContext context, out Guid userId)
    {
        if (context.Request.Headers.TryGetValue("X-User-Id", out var header)
            && Guid.TryParse(header.ToString(), out userId))
        {
            return true;
        }

        var claim = context.User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out userId);
    }


    private static bool IsAllowedRedirect(string returnUrl, IConfiguration configuration)
    {
        if (!Uri.TryCreate(returnUrl, UriKind.RelativeOrAbsolute, out var uri))
        {
            return false;
        }

        if (!uri.IsAbsoluteUri)
        {
            return returnUrl.StartsWith('/');
        }

        var allowedHosts = configuration.GetSection("OAuth:AllowedRedirectHosts").Get<string[]>() ?? Array.Empty<string>();
        return allowedHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase));
    }
}
