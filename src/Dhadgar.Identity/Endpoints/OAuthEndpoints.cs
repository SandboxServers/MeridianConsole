using Dhadgar.Identity.OAuth;
using Dhadgar.ServiceDefaults.Security;
using Microsoft.AspNetCore.Authentication;

namespace Dhadgar.Identity.Endpoints;

public static class OAuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/oauth/{provider}/link", BeginLink)
            .WithTags("OAuth")
            .WithName("BeginOAuthLink")
            .WithDescription("Begin OAuth provider account linking flow")
            .RequireRateLimiting("auth");
    }

    private static IResult BeginLink(
        HttpContext context,
        string provider,
        string? returnUrl,
        IConfiguration configuration,
        ISecurityEventLogger securityLogger)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString();

        if (!OAuthProviderRegistry.IsSupported(provider))
        {
            securityLogger.LogAuthorizationDenied(null, $"oauth/{provider}/link", "unsupported_provider", clientIp);
            return Results.NotFound();
        }

        // SECURITY FIX: Use centralized helper that only trusts JWT claims
        if (!EndpointHelpers.TryGetUserId(context, out var userId))
        {
            securityLogger.LogAuthenticationFailure(null, "oauth_link_unauthorized", clientIp, context.Request.Headers.UserAgent);
            return Results.Unauthorized();
        }

        var redirectTarget = "/";
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            if (IsAllowedRedirect(returnUrl, configuration))
            {
                redirectTarget = returnUrl;
            }
            else
            {
                // Log potential open redirect attempt
                securityLogger.LogSuspiciousActivity($"Invalid OAuth redirect attempt: {returnUrl}", userId.ToString(), clientIp);
            }
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectTarget,
            // SECURITY: Set state expiration to prevent replay attacks
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        properties.Items[OAuthLinkingHandler.LinkUserIdItem] = userId.ToString("D");

        return Results.Challenge(properties, new[] { provider });
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
