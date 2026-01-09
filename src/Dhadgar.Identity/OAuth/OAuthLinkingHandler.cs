using System.Security.Claims;
using AspNet.Security.OpenId;
using Dhadgar.Identity.Data.Entities;
using Dhadgar.Identity.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;

namespace Dhadgar.Identity.OAuth;

public static class OAuthLinkingHandler
{
    public const string LinkUserIdItem = "link_user_id";
    public const string ReturnUrlItem = "return_url";

    public static void Configure(OAuthOptions options, string provider)
    {
        options.Events ??= new OAuthEvents();
        options.Events.OnTicketReceived = context => HandleTicketReceivedAsync(context, provider);
    }

    public static void ConfigureOpenId(OpenIdAuthenticationOptions options, string provider)
    {
        options.Events ??= new OpenIdAuthenticationEvents();
        options.Events.OnAuthenticated = context => HandleOpenIdAuthenticatedAsync(context, provider);
    }

    private static async Task HandleTicketReceivedAsync(TicketReceivedContext context, string provider)
    {
        var success = await LinkAccountAsync(
            context.HttpContext,
            context.Principal,
            context.Properties,
            provider,
            identifierOverride: null,
            context.HttpContext.RequestAborted);

        if (!success)
        {
            context.Fail("link_failed");
        }
    }

    private static async Task HandleOpenIdAuthenticatedAsync(OpenIdAuthenticatedContext context, string provider)
    {
        var principal = context.Identity is null
            ? null
            : new ClaimsPrincipal(context.Identity);

        var success = await LinkAccountAsync(
            context.HttpContext,
            principal,
            context.Properties,
            provider,
            context.Identifier,
            context.HttpContext.RequestAborted);

        if (!success)
        {
            throw new InvalidOperationException("OpenId account linking failed.");
        }
    }

    private static async Task<bool> LinkAccountAsync(
        HttpContext httpContext,
        ClaimsPrincipal? principal,
        AuthenticationProperties? properties,
        string provider,
        string? identifierOverride,
        CancellationToken ct)
    {
        if (principal is null)
        {
            return false;
        }

        if (!TryGetUserId(properties, out var userId))
        {
            return false;
        }

        var providerAccountId = ResolveProviderAccountId(principal, identifierOverride);
        if (string.IsNullOrWhiteSpace(providerAccountId))
        {
            return false;
        }

        var metadata = BuildMetadata(principal);
        var linkedAccountService = httpContext.RequestServices.GetRequiredService<ILinkedAccountService>();
        var result = await linkedAccountService.LinkAsync(
            userId,
            new ExternalAccountInfo(provider, providerAccountId, metadata),
            ct);

        return result.Success;
    }

    private static bool TryGetUserId(AuthenticationProperties? properties, out Guid userId)
    {
        if (properties?.Items.TryGetValue(LinkUserIdItem, out var value) == true
            && Guid.TryParse(value, out userId))
        {
            return true;
        }

        userId = Guid.Empty;
        return false;
    }

    private static string? ResolveProviderAccountId(ClaimsPrincipal principal, string? identifierOverride)
    {
        var claimValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value
            ?? principal.FindFirst("id")?.Value;

        if (!string.IsNullOrWhiteSpace(claimValue))
        {
            return claimValue;
        }

        if (string.IsNullOrWhiteSpace(identifierOverride))
        {
            return null;
        }

        var segments = identifierOverride.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[^1] : identifierOverride;
    }

    private static LinkedAccountMetadata BuildMetadata(ClaimsPrincipal principal)
    {
        var metadata = new LinkedAccountMetadata
        {
            DisplayName = principal.FindFirst(ClaimTypes.Name)?.Value
                ?? principal.FindFirst("name")?.Value,
            Username = principal.FindFirst("preferred_username")?.Value
                ?? principal.FindFirst("login")?.Value,
            AvatarUrl = principal.FindFirst("avatar_url")?.Value
                ?? principal.FindFirst("avatar")?.Value
        };

        foreach (var claim in principal.Claims)
        {
            if (metadata.ExtraData.ContainsKey(claim.Type))
            {
                continue;
            }

            metadata.ExtraData[claim.Type] = claim.Value;
        }

        return metadata;
    }
}
