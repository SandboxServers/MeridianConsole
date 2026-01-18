using System.Collections.Generic;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Custom token handler for Azure Workload Identity Federation compatibility.
/// Azure WIF requires JWT tokens with typ: JWT header, but OpenIddict uses at+jwt by default.
///
/// This handler intercepts token generation and modifies the typ header for WIF tokens.
/// </summary>
public static class AzureWifTokenHandler
{
    private const string WifScope = "wif";

    /// <summary>
    /// Applies custom token formatting before OpenIddict signs the token.
    /// Replaces the JWT typ header from "at+jwt" to "JWT" for Azure WIF compatibility.
    /// </summary>
    public static ValueTask ApplyAccessTokenType(OpenIddictServerEvents.GenerateTokenContext context)
    {
        if (!IsWifAccessToken(context))
        {
            return default;
        }

        if (context.SecurityTokenDescriptor is SecurityTokenDescriptor descriptor)
        {
            descriptor.TokenType = OpenIddictConstants.JsonWebTokenTypes.GenericJsonWebToken;
        }

        return default;
    }

    private static bool IsWifAccessToken(OpenIddictServerEvents.GenerateTokenContext context)
    {
        if (!string.Equals(context.TokenFormat, OpenIddictConstants.TokenFormats.Private.JsonWebToken, StringComparison.Ordinal))
        {
            return false;
        }

        var tokenType = context.TokenType;
        if (string.IsNullOrWhiteSpace(tokenType))
        {
            return false;
        }

        if (!string.Equals(tokenType, OpenIddictConstants.TokenTypeIdentifiers.AccessToken, StringComparison.Ordinal) &&
            !string.Equals(tokenType, OpenIddictConstants.TokenTypeHints.AccessToken, StringComparison.Ordinal) &&
            !string.Equals(tokenType, OpenIddictConstants.JsonWebTokenTypes.AccessToken, StringComparison.Ordinal))
        {
            return false;
        }

        if (context.Principal is not null && ContainsWifScope(context.Principal.GetScopes()))
        {
            return true;
        }

        return context.Request is not null && ContainsWifScope(context.Request.GetScopes());
    }

    private static bool ContainsWifScope(IEnumerable<string> scopes)
    {
        foreach (var scope in scopes)
        {
            if (string.Equals(scope, WifScope, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
