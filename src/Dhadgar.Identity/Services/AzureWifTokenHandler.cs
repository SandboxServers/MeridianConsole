using OpenIddict.Server;
using System.Text;
using System.Text.Json;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Custom token handler for Azure Workload Identity Federation compatibility.
/// Azure WIF requires JWT tokens with typ: JWT header, but OpenIddict uses at+jwt by default.
///
/// This handler intercepts token generation and modifies the typ header for WIF tokens.
/// </summary>
public static class AzureWifTokenHandler
{
    /// <summary>
    /// Applies custom token formatting after OpenIddict generates the token.
    /// Replaces the JWT typ header from "at+jwt" to "JWT" for Azure WIF compatibility.
    /// </summary>
    public static ValueTask ApplyTokenResponse(OpenIddictServerEvents.ApplyTokenResponseContext context)
    {
        // Only process successful token responses with WIF scope
        if (string.IsNullOrEmpty(context.Response.AccessToken))
        {
            return default;
        }

        // Check if this request included the WIF scope
        var request = context.Transaction.Request;
        if (request is null || request.Scope is null)
        {
            return default;
        }

        var scopes = request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains("wif", StringComparer.OrdinalIgnoreCase))
        {
            return default;
        }

        // At this point, OpenIddict has already generated the token with typ: at+jwt
        // We need to manually replace it with typ: JWT

        try
        {
            var token = context.Response.AccessToken!;
            var parts = token.Split('.');

            if (parts.Length != 3)
            {
                return default; // Not a valid JWT
            }

            // Decode the header
            var headerJson = Base64UrlDecode(parts[0]);
            var header = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(headerJson);

            if (header is null)
            {
                return default;
            }

            // Check if typ is at+jwt
            if (header.TryGetValue("typ", out var typValue) &&
                typValue.GetString() == "at+jwt")
            {
                // Replace typ with JWT
                header["typ"] = JsonSerializer.SerializeToElement("JWT");

                // Re-encode the header
                var newHeaderJson = JsonSerializer.Serialize(header);
                var newHeaderBase64 = Base64UrlEncode(newHeaderJson);

                // Reconstruct the token (header is modified, payload and signature unchanged)
                var newToken = $"{newHeaderBase64}.{parts[1]}.{parts[2]}";

                // Replace the access token in the response
                context.Response.AccessToken = newToken;
            }
        }
        catch
        {
            // If anything fails, leave the original token intact
            // This ensures we don't break existing functionality
        }

        return default;
    }

    private static string Base64UrlDecode(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string Base64UrlEncode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var base64 = Convert.ToBase64String(bytes);
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
